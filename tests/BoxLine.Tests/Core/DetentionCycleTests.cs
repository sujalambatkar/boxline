using BoxLine.Core.Detention;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class DetentionCycleTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static ContainerMovement Move(int id, MovementEventType type, double days, int? shipperId = null, string? booking = null) => new()
    {
        Id = id,
        EventType = type,
        OccurredAt = T0.AddDays(days),
        BookingId = booking is null ? null : id,
        Booking = booking is null ? null : new Booking { BookingNumber = booking, ShipperId = shipperId ?? 0 },
    };

    private static List<ContainerMovement> OneCycle() =>
    [
        Move(1, MovementEventType.GateOutEmpty, 0),
        Move(2, MovementEventType.Stuffed, 1),
        Move(3, MovementEventType.GateInFull, 2),
        Move(4, MovementEventType.LoadedOnVessel, 3),
        Move(5, MovementEventType.Discharged, 20),
        Move(6, MovementEventType.GateOutFull, 22),
        Move(7, MovementEventType.DeliveredToConsignee, 23),
        Move(8, MovementEventType.EmptyReturned, 30),
    ];

    private static DetentionClock? Find(IReadOnlyList<ContainerMovement> history, Func<ContainerMovement, bool>? candidates = null) =>
        DetentionCycle.Find(history, candidates ?? (_ => true));

    [Fact]
    public void TheClockStartsAtGateOutFull_AndStopsAtEmptyReturned()
    {
        var clock = Find(OneCycle())!;

        clock.Start.Should().Be(new DateOnly(2026, 11, 23));
        clock.End.Should().Be(new DateOnly(2026, 12, 1));
        clock.StartedBy.Should().Be(MovementEventType.GateOutFull);
    }

    [Fact]
    public void WithoutAReturn_TheClockIsStillRunning()
    {
        var history = OneCycle().Take(7).ToList();

        var clock = Find(history)!;

        clock.End.Should().BeNull();
        clock.Start.Should().Be(new DateOnly(2026, 11, 23));
    }

    [Fact]
    public void DeliveryStartsTheClock_WhenNoGateOutWasRecorded()
    {
        var history = OneCycle().Where(e => e.EventType != MovementEventType.GateOutFull).ToList();

        var clock = Find(history)!;

        clock.StartedBy.Should().Be(MovementEventType.DeliveredToConsignee);
        clock.Start.Should().Be(new DateOnly(2026, 11, 24));
    }

    [Fact]
    public void NoClock_BeforeTheContainerLeavesTheTerminal()
    {
        Find(OneCycle().Take(5).ToList()).Should().BeNull();
        Find([]).Should().BeNull();
    }

    [Fact]
    public void TheLatestCycleWins_WhenTheContainerHasBeenAroundTwice()
    {
        var history = OneCycle();
        history.AddRange(
        [
            Move(9, MovementEventType.GateOutEmpty, 40),
            Move(10, MovementEventType.Stuffed, 41),
            Move(11, MovementEventType.GateInFull, 42),
            Move(12, MovementEventType.LoadedOnVessel, 43),
            Move(13, MovementEventType.Discharged, 60),
            Move(14, MovementEventType.GateOutFull, 61),
        ]);

        var clock = Find(history)!;

        clock.Start.Should().Be(new DateOnly(2027, 1, 1));
        clock.End.Should().BeNull("the second cycle has not come back yet; the first cycle's return must not be reused");
    }

    [Fact]
    public void TheReturnIsFoundEvenWhenItIsNotLinkedToTheBooking()
    {
        var history = OneCycle();
        history[5] = Move(6, MovementEventType.GateOutFull, 22, shipperId: 7, booking: "BL-202611-0001");

        var clock = Find(history, e => e.Booking?.BookingNumber == "BL-202611-0001")!;

        clock.End.Should().Be(new DateOnly(2026, 12, 1));
    }

    [Fact]
    public void OnlyCandidateEventsCanStartTheClock()
    {
        var history = OneCycle();
        history[5] = Move(6, MovementEventType.GateOutFull, 22, shipperId: 7, booking: "BL-A");
        history.AddRange(
        [
            Move(9, MovementEventType.GateOutEmpty, 40),
            Move(10, MovementEventType.Stuffed, 41),
            Move(11, MovementEventType.GateInFull, 42),
            Move(12, MovementEventType.LoadedOnVessel, 43),
            Move(13, MovementEventType.Discharged, 60),
            Move(14, MovementEventType.GateOutFull, 61, shipperId: 8, booking: "BL-B"),
        ]);

        Find(history, e => e.Booking?.ShipperId == 7)!.Start.Should().Be(new DateOnly(2026, 11, 23));
        Find(history, e => e.Booking?.ShipperId == 8)!.Start.Should().Be(new DateOnly(2027, 1, 1));
        Find(history, e => e.Booking?.ShipperId == 99).Should().BeNull();
    }

    [Fact]
    public void TheDateIsTheUtcCalendarDayOfTheEvent()
    {
        var history = OneCycle();
        history[5] = new ContainerMovement { Id = 6, EventType = MovementEventType.GateOutFull, OccurredAt = new DateTime(2026, 11, 23, 23, 59, 0, DateTimeKind.Utc) };

        Find(history)!.Start.Should().Be(new DateOnly(2026, 11, 23));
    }
}
