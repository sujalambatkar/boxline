using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class ScheduleFinderTests
{
    private static readonly DateTime Today = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A voyage Nhava Sheva (1) to Jebel Ali (3) to Rotterdam (4), leaving Nhava Sheva <paramref name="departsInDays"/> days from Today.</summary>
    private static Voyage Voyage(int id, double departsInDays, VoyageStatus status = VoyageStatus.Scheduled) => new()
    {
        Id = id,
        VoyageNumber = $"V{id}",
        Status = status,
        Calls =
        [
            new() { PortId = 1, Sequence = 1, Etd = Today.AddDays(departsInDays) },
            new() { PortId = 3, Sequence = 2, Eta = Today.AddDays(departsInDays + 5), Etd = Today.AddDays(departsInDays + 5.5) },
            new() { PortId = 4, Sequence = 3, Eta = Today.AddDays(departsInDays + 20) },
        ],
    };

    private static IReadOnlyList<int> Ids(IEnumerable<Voyage> voyages, int pol = 1, int pod = 4, int fromDay = 0, int toDay = 30) =>
        ScheduleFinder.Find(voyages, pol, pod, DateOnly.FromDateTime(Today.AddDays(fromDay)), DateOnly.FromDateTime(Today.AddDays(toDay)))
            .Select(o => o.Voyage.Id).ToList();

    [Fact]
    public void Find_ReturnsMatchingVoyagesEarliestDepartureFirst()
    {
        var voyages = new[] { Voyage(1, 14), Voyage(2, 3), Voyage(3, 7) };

        Ids(voyages).Should().Equal(2, 3, 1);
    }

    [Fact]
    public void Find_ExposesTheCallsAtBothEnds()
    {
        var option = ScheduleFinder.Find([Voyage(1, 3)], 1, 4, DateOnly.FromDateTime(Today), DateOnly.FromDateTime(Today.AddDays(30))).Single();

        option.Pol.PortId.Should().Be(1);
        option.Pod.PortId.Should().Be(4);
        option.Pol.Etd.Should().Be(Today.AddDays(3));
        option.Pod.Eta.Should().Be(Today.AddDays(23));
    }

    [Fact]
    public void Find_IncludesBothEndsOfTheDateWindow()
    {
        var voyages = new[] { Voyage(1, 0), Voyage(2, 30), Voyage(3, 31), Voyage(4, -1) };

        Ids(voyages, fromDay: 0, toDay: 30).Should().Equal(1, 2);
    }

    [Fact]
    public void Find_OnlyReturnsVoyagesThatSailInTheRequestedDirection()
    {
        var voyages = new[] { Voyage(1, 3) };

        Ids(voyages, pol: 4, pod: 1).Should().BeEmpty();
        Ids(voyages, pol: 1, pod: 3).Should().Equal(1);
        Ids(voyages, pol: 3, pod: 4).Should().Equal(1);
        Ids(voyages, pol: 1, pod: 99).Should().BeEmpty();
    }

    [Theory]
    [InlineData(VoyageStatus.Scheduled, true)]
    [InlineData(VoyageStatus.Underway, true)]
    [InlineData(VoyageStatus.Completed, false)]
    [InlineData(VoyageStatus.Cancelled, false)]
    public void Find_SkipsFinishedVoyages(VoyageStatus status, bool expected)
    {
        Ids([Voyage(1, 3, status)]).Any().Should().Be(expected);
    }

    [Fact]
    public void Find_SkipsAnUnderwayVoyageThatHasAlreadyLeftTheLoadingPort()
    {
        var departed = Voyage(1, 3, VoyageStatus.Underway);
        departed.Calls[0].Atd = Today.AddDays(3);
        var notYet = Voyage(2, 3, VoyageStatus.Underway);

        Ids([departed, notYet]).Should().Equal(2);
        // Loading at Jebel Ali is still possible on the departed voyage, because it has not left there.
        Ids([departed], pol: 3, pod: 4, toDay: 40).Should().Equal(1);
    }

    [Fact]
    public void Find_SkipsVoyagesWithoutAKnownDepartureOrArrival()
    {
        var noEtd = Voyage(1, 3);
        noEtd.Calls[0].Etd = null;
        var noEta = Voyage(2, 3);
        noEta.Calls[2].Eta = null;

        Ids([noEtd, noEta]).Should().BeEmpty();
    }

    [Fact]
    public void Find_UsesTheUtcCalendarDayOfTheDeparture()
    {
        var lateEvening = Voyage(1, 0);
        lateEvening.Calls[0].Etd = Today.AddHours(23).AddMinutes(30);

        Ids([lateEvening], fromDay: 0, toDay: 0).Should().Equal(1);
        Ids([lateEvening], fromDay: 1, toDay: 5).Should().BeEmpty();
    }
}
