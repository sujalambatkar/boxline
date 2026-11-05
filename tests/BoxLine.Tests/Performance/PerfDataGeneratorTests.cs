using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Movements;
using BoxLine.PerfTool;
using FluentAssertions;

namespace BoxLine.Tests.Performance;

public class PerfDataGeneratorTests
{
    private static readonly DateTime End = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
    private const int Containers = 300;
    private const int Total = 4_000;
    private const int Span = 730;

    private static List<PerfMovement> Generate(int seed = 1, int containers = Containers, int total = Total) =>
        PerfDataGenerator.Movements(containers, total, End, Span, seed).ToList();

    [Fact]
    public void ProducesExactlyTheRequestedNumberOfMovements() => Generate().Should().HaveCount(Total);

    [Fact]
    public void IsRepeatable_AndDifferentSeedsGiveDifferentData()
    {
        Generate(7).Should().Equal(Generate(7));
        Generate(7).Should().NotEqual(Generate(8));
    }

    [Fact]
    public void ContainersHaveValidUniqueIso6346Numbers()
    {
        var containers = PerfDataGenerator.Containers(2_000, typeCount: 6, seed: 1);

        containers.Should().HaveCount(2_000);
        containers.Select(c => c.Number).Should().OnlyHaveUniqueItems().And.OnlyContain(n => Iso6346Validator.IsValid(n));
        containers.Select(c => c.TypeIndex).Distinct().Should().HaveCount(6);
        containers.Should().Contain(c => c.Ownership == OwnershipType.Owned).And.Contain(c => c.Ownership == OwnershipType.Leased);
    }

    [Fact]
    public void EveryContainersHistory_ObeysTheApplicationsOwnStateMachine()
    {
        var byContainer = Generate().GroupBy(m => m.ContainerIndex);

        foreach (var history in byContainer)
        {
            LastMovement? last = null;
            foreach (var movement in history.OrderBy(m => m.OccurredAt))
            {
                var act = () => MovementStateMachine.Validate(last, movement.EventType, movement.OccurredAt, End.AddDays(1));
                act.Should().NotThrow($"container {history.Key}: {movement.EventType} at {movement.OccurredAt:u}");
                last = new LastMovement(movement.EventType, movement.OccurredAt);
            }
        }
    }

    [Fact]
    public void EveryMovement_FallsInsideTheRequestedSpan()
    {
        var movements = Generate();

        movements.Min(m => m.OccurredAt).Should().BeOnOrAfter(End.AddDays(-Span));
        movements.Max(m => m.OccurredAt).Should().BeOnOrBefore(End);
    }

    [Fact]
    public void TheHistory_ReachesBothEndsOfTheWindow_NotStoppingAWorstCaseCycleShort()
    {
        var movements = Generate(containers: 600, total: 8_000);

        movements.Max(m => m.OccurredAt).Should().BeAfter(End.AddDays(-15), "there is activity right up to the end date");
        movements.Min(m => m.OccurredAt).Should().BeBefore(End.AddDays(-Span + 15), "and right from the start of the window");
    }

    [Fact]
    public void TheHistoryReachesTheMostRecentMonths_SoALastTwelveMonthsReportHasData()
    {
        var movements = Generate(containers: 600, total: 8_000);

        movements.Count(m => m.OccurredAt >= End.AddDays(-365)).Should().BeGreaterThan(movements.Count / 4);
        movements.Count(m => m.OccurredAt >= End.AddDays(-90)).Should().BeGreaterThan(0);
        movements.Count(m => m.OccurredAt < End.AddDays(-640)).Should().BeGreaterThan(0, "the earliest months have data too");
    }

    [Fact]
    public void EachCycleHappensWhereItShould_AndTheNextOneStartsWhereTheLastEnded()
    {
        foreach (var history in Generate().GroupBy(m => m.ContainerIndex))
        {
            var events = history.OrderBy(m => m.OccurredAt).ToList();
            for (var i = 0; i + 3 < events.Count; i += 8)
            {
                events[i].EventType.Should().Be(MovementEventType.GateOutEmpty);
                var origin = events[i].Location;
                events.Skip(i).Take(Math.Min(4, events.Count - i)).Should().OnlyContain(m => m.Location == origin, "loading side events share the origin");

                if (i + 7 < events.Count)
                {
                    var destination = events[i + 4].Location;
                    destination.Should().NotBe(origin);
                    events.Skip(i + 4).Take(4).Should().OnlyContain(m => m.Location == destination, "discharge side events share the destination");

                    if (i + 8 < events.Count)
                    {
                        events[i + 8].Location.Should().Be(destination, "the empty container leaves from where it was returned");
                    }
                }
            }
        }
    }

    [Fact]
    public void AllEightEventTypesAppear_InRoughlyEqualNumbers()
    {
        var counts = Generate(containers: 600, total: 8_000).GroupBy(m => m.EventType).ToDictionary(g => g.Key, g => g.Count());

        counts.Keys.Should().BeEquivalentTo(Enum.GetValues<MovementEventType>());
        counts.Values.Max().Should().BeLessThan(counts.Values.Min() * 2);
    }

    [Fact]
    public void TrafficIsSkewedTowardsTheBusiestPorts()
    {
        var byLocation = Generate(containers: 600, total: 8_000).GroupBy(m => m.Location).ToDictionary(g => g.Key, g => g.Count());

        byLocation["CNSHA"].Should().BeGreaterThan(byLocation["INBLR"] * 2);
        byLocation.Keys.Should().OnlyContain(l => PerfDataGenerator.Locations.Contains(l));
        byLocation.Keys.Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void TheLastCycleMayBeCutShort_ButStaysAValidPrefix()
    {
        // 10 movements is one whole cycle plus two events of the next.
        var movements = PerfDataGenerator.Movements(containerCount: 1, total: 10, End, Span, seed: 3).ToList();

        movements.Select(m => m.EventType).Should().Equal(
            MovementEventType.GateOutEmpty, MovementEventType.Stuffed, MovementEventType.GateInFull, MovementEventType.LoadedOnVessel,
            MovementEventType.Discharged, MovementEventType.GateOutFull, MovementEventType.DeliveredToConsignee, MovementEventType.EmptyReturned,
            MovementEventType.GateOutEmpty, MovementEventType.Stuffed);
    }

    [Fact]
    public void Refuses_WhenTheFleetIsTooSmallForTheRequestedVolume()
    {
        var act = () => PerfDataGenerator.Movements(containerCount: 5, total: 5_000, End, Span, seed: 1).ToList();

        act.Should().Throw<InvalidOperationException>().WithMessage("*could be generated*");
    }

    [Fact]
    public void Refuses_ASpanTooShortForAContainersCycles()
    {
        var act = () => PerfDataGenerator.Movements(10, 100, End, spanDays: 200, seed: 1).ToList();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheIdleContainers_WhenTheQuotaRunsOutEarly_AreScatteredNotBunchedAtTheEnd()
    {
        // Few movements for many containers: only some get any. They should not all be the first ones.
        var used = PerfDataGenerator.Movements(containerCount: 1_000, total: 800, End, Span, seed: 2).Select(m => m.ContainerIndex).Distinct().ToList();

        used.Should().HaveCountGreaterThan(30);
        used.Max().Should().BeGreaterThan(500, "the scrambled walk reaches containers far from the start of the list");
    }
}
