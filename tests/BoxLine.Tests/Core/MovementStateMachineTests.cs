using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Movements;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class MovementStateMachineTests
{
    private static readonly MovementEventType[] Cycle =
    [
        MovementEventType.GateOutEmpty,
        MovementEventType.Stuffed,
        MovementEventType.GateInFull,
        MovementEventType.LoadedOnVessel,
        MovementEventType.Discharged,
        MovementEventType.GateOutFull,
        MovementEventType.DeliveredToConsignee,
        MovementEventType.EmptyReturned,
    ];

    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Every (last event, next event) pair: 9 starting states (including "no history") by 8 events.</summary>
    public static TheoryData<MovementEventType?, MovementEventType, bool> EveryPair()
    {
        var data = new TheoryData<MovementEventType?, MovementEventType, bool>();
        var starts = new MovementEventType?[] { null }.Concat(Cycle.Select(e => (MovementEventType?)e));

        foreach (var last in starts)
        {
            // The only legal follow-up is the next stage of the cycle (or gate out empty for a new container).
            var expected = last is null
                ? MovementEventType.GateOutEmpty
                : Cycle[(Array.IndexOf(Cycle, last.Value) + 1) % Cycle.Length];

            foreach (var next in Cycle)
            {
                data.Add(last, next, next == expected);
            }
        }

        return data;
    }

    [Fact]
    public void EveryPair_CoversAllSeventyTwoCombinations() => EveryPair().Should().HaveCount(72);

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void IsAllowed_AcceptsOnlyTheNextStageOfTheCycle(MovementEventType? last, MovementEventType next, bool expected)
    {
        MovementStateMachine.IsAllowed(last, next).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Validate_ThrowsConflictExactlyWhenThePairIsNotAllowed(MovementEventType? last, MovementEventType next, bool allowed)
    {
        var previous = last is null ? null : new LastMovement(last.Value, Now.AddHours(-1));

        var act = () => MovementStateMachine.Validate(previous, next, Now, Now);

        if (allowed)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<ConflictException>().WithMessage($"{next} cannot follow*Expected:*");
        }
    }

    [Fact]
    public void AllowedNext_ForANewContainer_IsGateOutEmptyOnly() =>
        MovementStateMachine.AllowedNext(null).Should().Equal(MovementEventType.GateOutEmpty);

    [Fact]
    public void AllowedNext_AfterEmptyReturned_StartsANewCycle() =>
        MovementStateMachine.AllowedNext(MovementEventType.EmptyReturned).Should().Equal(MovementEventType.GateOutEmpty);

    [Fact]
    public void TheCycle_VisitsEveryEventTypeExactlyOnce() =>
        Cycle.Should().BeEquivalentTo(Enum.GetValues<MovementEventType>()).And.OnlyHaveUniqueItems();

    [Fact]
    public void Validate_RejectsAnEventBeforeThePreviousOne()
    {
        var last = new LastMovement(MovementEventType.GateOutEmpty, Now.AddHours(-1));

        var act = () => MovementStateMachine.Validate(last, MovementEventType.Stuffed, Now.AddHours(-1).AddMinutes(-1), Now);

        act.Should().Throw<DomainValidationException>().Which.Errors["OccurredAt"].Single().Should().Contain("earlier than the previous event");
    }

    [Fact]
    public void Validate_AllowsAnEventAtTheSameTimeAsThePreviousOne()
    {
        var last = new LastMovement(MovementEventType.GateOutEmpty, Now);

        MovementStateMachine.Validate(last, MovementEventType.Stuffed, Now, Now);
    }

    [Fact]
    public void Validate_RejectsAnEventInTheFuture_ButToleratesSmallClockDifferences()
    {
        MovementStateMachine.Validate(null, MovementEventType.GateOutEmpty, Now + MovementStateMachine.FutureTolerance, Now);

        var act = () => MovementStateMachine.Validate(null, MovementEventType.GateOutEmpty, Now + MovementStateMachine.FutureTolerance + TimeSpan.FromSeconds(1), Now);

        act.Should().Throw<DomainValidationException>().Which.Errors["OccurredAt"].Single().Should().Contain("future");
    }

    [Fact]
    public void Validate_ReportsAnOutOfOrderEventBeforeATimeProblem()
    {
        var last = new LastMovement(MovementEventType.GateOutEmpty, Now.AddHours(-1));

        var act = () => MovementStateMachine.Validate(last, MovementEventType.Discharged, Now.AddDays(-5), Now);

        act.Should().Throw<ConflictException>();
    }
}
