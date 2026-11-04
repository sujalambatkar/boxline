using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class VoyageRotationTests
{
    private static readonly DateTime Day0 = new(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);

    // Ports: 1 Nhava Sheva, 2 Mundra, 3 Jebel Ali, 4 Rotterdam.
    private static CallDraft Call(int sequence, int port, double? etaDays, double? etdDays) =>
        new(sequence, port, etaDays is { } a ? Day0.AddDays(a) : null, etdDays is { } d ? Day0.AddDays(d) : null);

    private static List<CallDraft> ValidPlan() =>
    [
        Call(1, 1, null, 0),
        Call(2, 2, 2, 2.5),
        Call(3, 3, 5, 5.5),
        Call(4, 4, 20, null),
    ];

    private static string[] Problems(IReadOnlyList<CallDraft> calls) =>
        VoyageRotation.ValidatePlan(calls).GetValueOrDefault(VoyageRotation.ErrorKey) ?? [];

    // ---- validating a plan ------------------------------------------------------------------------

    [Fact]
    public void ValidatePlan_AcceptsAWellFormedRotation() => VoyageRotation.ValidatePlan(ValidPlan()).Should().BeEmpty();

    [Fact]
    public void ValidatePlan_AcceptsASimpleTwoPortRotation() =>
        VoyageRotation.ValidatePlan([Call(1, 1, null, 0), Call(2, 4, 14, null)]).Should().BeEmpty();

    [Fact]
    public void ValidatePlan_NeedsAtLeastTwoCalls()
    {
        Problems([Call(1, 1, null, 0)]).Should().Contain("A rotation needs at least two port calls.");
        Problems([]).Should().Contain("A rotation needs at least two port calls.");
    }

    [Fact]
    public void ValidatePlan_RequiresSequenceToIncreaseStrictly()
    {
        var repeated = ValidPlan();
        repeated[2] = Call(2, 3, 5, 5.5);
        Problems(repeated).Should().Contain(p => p.StartsWith("Call 3: sequence 2 must be greater than the previous call's (2)"));

        var backwards = ValidPlan();
        backwards[3] = Call(1, 4, 20, null);
        Problems(backwards).Should().Contain(p => p.StartsWith("Call 4: sequence 1 must be greater"));
    }

    [Fact]
    public void ValidatePlan_AllowsGapsInSequenceNumbers()
    {
        VoyageRotation.ValidatePlan([Call(10, 1, null, 0), Call(20, 2, 2, 2.5), Call(30, 4, 9, null)]).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidatePlan_RequiresAPositiveSequence(int sequence)
    {
        Problems([Call(sequence, 1, null, 0), Call(2, 4, 14, null)]).Should().Contain("Call 1: sequence must be 1 or more.");
    }

    [Fact]
    public void ValidatePlan_RejectsAPortVisitedTwice()
    {
        var plan = ValidPlan();
        plan[3] = Call(4, 2, 20, null);

        Problems(plan).Should().Contain("A port can appear only once in a rotation (calls 2 and 4).");
    }

    [Fact]
    public void ValidatePlan_RequiresDepartureAtTheFirstPortAndArrivalAtTheLast()
    {
        var plan = ValidPlan();
        plan[0] = Call(1, 1, null, null);
        plan[3] = Call(4, 4, null, null);

        var problems = Problems(plan);

        problems.Should().Contain("Call 1 (first port): ETD is required.");
        problems.Should().Contain("Call 4 (last port): ETA is required.");
    }

    [Fact]
    public void ValidatePlan_RequiresBothTimesAtIntermediatePorts()
    {
        var plan = ValidPlan();
        plan[1] = Call(2, 2, 2, null);

        Problems(plan).Should().Contain("Call 2: both ETA and ETD are required for an intermediate port.");
    }

    [Fact]
    public void ValidatePlan_RejectsDepartureBeforeArrivalAtAPort()
    {
        var plan = ValidPlan();
        plan[1] = Call(2, 2, 2.5, 2);

        Problems(plan).Should().Contain("Call 2: ETD cannot be before ETA.");
    }

    [Fact]
    public void ValidatePlan_AllowsDepartureAtTheSameInstantAsArrival()
    {
        var plan = ValidPlan();
        plan[1] = Call(2, 2, 2, 2);

        VoyageRotation.ValidatePlan(plan).Should().BeEmpty();
    }

    [Fact]
    public void ValidatePlan_RejectsArrivingBeforeThePreviousPortDeparts()
    {
        var plan = ValidPlan();
        plan[2] = Call(3, 3, 2.25, 2.4);

        Problems(plan).Should().Contain("Call 3: arrival is before call 2 departs.");
    }

    [Fact]
    public void ValidatePlan_ReportsEveryProblemAtOnce()
    {
        var problems = Problems([Call(1, 1, null, null), Call(1, 1, null, null)]);

        problems.Should().HaveCountGreaterThan(3);
    }

    // ---- matching a booking's route ---------------------------------------------------------------

    private static List<VoyageCall> Calls() =>
    [
        new() { Id = 1, PortId = 1, Sequence = 1, Etd = Day0 },
        new() { Id = 2, PortId = 2, Sequence = 2, Eta = Day0.AddDays(2), Etd = Day0.AddDays(2.5) },
        new() { Id = 3, PortId = 3, Sequence = 3, Eta = Day0.AddDays(5), Etd = Day0.AddDays(5.5) },
        new() { Id = 4, PortId = 4, Sequence = 4, Eta = Day0.AddDays(20) },
    ];

    [Theory]
    [InlineData(1, 4)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void FindLeg_FindsThePairWhenLoadingComesBeforeDischarge(int pol, int pod)
    {
        var leg = VoyageRotation.FindLeg(Calls(), pol, pod);

        leg.Should().NotBeNull();
        leg!.Value.Pol.PortId.Should().Be(pol);
        leg.Value.Pod.PortId.Should().Be(pod);
    }

    [Theory]
    [InlineData(4, 1, "reversed")]
    [InlineData(3, 2, "reversed")]
    [InlineData(2, 2, "same port")]
    [InlineData(1, 99, "discharge port not on the voyage")]
    [InlineData(99, 4, "loading port not on the voyage")]
    [InlineData(98, 99, "neither port on the voyage")]
    public void FindLeg_ReturnsNullOtherwise(int pol, int pod, string why)
    {
        VoyageRotation.FindLeg(Calls(), pol, pod).Should().BeNull(why);
    }

    [Fact]
    public void FindLeg_DoesNotDependOnTheOrderTheCallsAreListedIn()
    {
        var shuffled = Calls().AsEnumerable().Reverse().ToList();

        VoyageRotation.FindLeg(shuffled, 1, 4).Should().NotBeNull();
        VoyageRotation.FindLeg(shuffled, 4, 1).Should().BeNull();
    }

    [Fact]
    public void EnsureServes_ThrowsWithTheUnLocodesInTheMessage()
    {
        var voyage = new Voyage { VoyageNumber = "BXL2612E", Calls = Calls() };
        var rotterdam = new Port { Id = 4, UnLocode = "NLRTM" };
        var nhavaSheva = new Port { Id = 1, UnLocode = "INNSA" };

        VoyageRotation.EnsureServes(voyage, nhavaSheva, rotterdam);
        var act = () => VoyageRotation.EnsureServes(voyage, rotterdam, nhavaSheva);

        act.Should().Throw<DomainValidationException>().Which.Errors["VoyageId"].Single()
            .Should().Be("Voyage BXL2612E does not sail from NLRTM to INNSA in that order.");
    }

    // ---- is a voyage still taking bookings --------------------------------------------------------

    [Theory]
    [InlineData(VoyageStatus.Scheduled, true)]
    [InlineData(VoyageStatus.Underway, true)]
    [InlineData(VoyageStatus.Completed, false)]
    [InlineData(VoyageStatus.Cancelled, false)]
    public void IsOpenForBooking_DependsOnStatus_WhenNothingHasDepartedYet(VoyageStatus status, bool expected)
    {
        var voyage = new Voyage { Status = status, Calls = Calls() };

        VoyageRotation.IsOpenForBooking(voyage, 1).Should().Be(expected);
    }

    [Fact]
    public void IsOpenForBooking_ClosesForAnUnderwayVoyageOnceItHasLeftTheLoadingPort()
    {
        var calls = Calls();
        calls[0].Atd = Day0;
        calls[1].Ata = Day0.AddDays(2);
        var voyage = new Voyage { Status = VoyageStatus.Underway, Calls = calls };

        VoyageRotation.IsOpenForBooking(voyage, 1).Should().BeFalse("it has already left the first port");
        VoyageRotation.IsOpenForBooking(voyage, 2).Should().BeTrue("it has not yet left the second port");
        VoyageRotation.IsOpenForBooking(voyage, 99).Should().BeFalse("the port is not on the voyage");
    }

    [Fact]
    public void DepartureAndArrival_ReadTheEstimatesAtTheRightPorts()
    {
        VoyageRotation.Departure(Calls(), 1).Should().Be(Day0);
        VoyageRotation.Arrival(Calls(), 4).Should().Be(Day0.AddDays(20));
        VoyageRotation.Departure(Calls(), 99).Should().BeNull();
    }
}
