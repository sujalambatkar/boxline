using BoxLine.Core.Common;
using BoxLine.Core.Reports;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class UtilisationReportServiceTests
{
    private sealed class FakeReport(RawUtilisation result) : IUtilisationReport
    {
        public UtilisationQuery? Received { get; private set; }

        public Task<RawUtilisation> RunAsync(UtilisationQuery query, CancellationToken cancellationToken)
        {
            Received = query;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
    private static readonly RawUtilisation Empty = new([], 0, 200, []);

    private static (UtilisationReportService Service, FakeReport Report) Create(RawUtilisation? result = null)
    {
        var report = new FakeReport(result ?? Empty);
        return (new UtilisationReportService(report, Clock), report);
    }

    // ---- percentages ------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 200, 0.0)]
    [InlineData(1, 200, 0.5)]
    [InlineData(1, 3, 33.3)]
    [InlineData(2, 3, 66.7)]
    [InlineData(200, 200, 100.0)]
    [InlineData(5, 0, 0.0)]
    [InlineData(1, 8, 12.5)]
    [InlineData(1, 16, 6.3)]
    public void Percent_IsOneDecimal_RoundedAwayFromZero_AndSafeForAnEmptyFleet(int containers, int fleet, double expected) =>
        Utilisation.Percent(containers, fleet).Should().Be((decimal)expected);

    // ---- defaults ---------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutAMonthRange_ItCoversTheLatestTwelveMonths()
    {
        var (service, report) = Create();

        await service.RunAsync(new UtilisationQuery(), default);

        report.Received!.FromMonth.Should().Be(new DateOnly(2025, 12, 1));
        report.Received.ToMonth.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public async Task AnyDayInAMonthMeansThatMonth()
    {
        var (service, report) = Create();

        await service.RunAsync(new UtilisationQuery(new DateOnly(2026, 3, 17), new DateOnly(2026, 5, 31)), default);

        report.Received!.FromMonth.Should().Be(new DateOnly(2026, 3, 1));
        report.Received.ToMonth.Should().Be(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task AnOpenEndedRange_IsKeptOpen()
    {
        var (service, report) = Create();

        await service.RunAsync(new UtilisationQuery(FromMonth: new DateOnly(2026, 3, 1)), default);

        report.Received!.FromMonth.Should().Be(new DateOnly(2026, 3, 1));
        report.Received.ToMonth.Should().BeNull();
    }

    [Fact]
    public async Task TheLocationIsNormalised()
    {
        var (service, report) = Create();

        await service.RunAsync(new UtilisationQuery(Location: " innsa "), default);

        report.Received!.Location.Should().Be("INNSA");
    }

    // ---- validation -------------------------------------------------------------------------------

    [Fact]
    public async Task ALastMonthBeforeTheFirst_IsRejected()
    {
        var (service, _) = Create();

        var act = () => service.RunAsync(new UtilisationQuery(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1)), default);

        (await act.Should().ThrowAsync<DomainValidationException>()).Which.Errors.Should().ContainKey("ToMonth");
    }

    [Theory]
    [InlineData("INN")]
    [InlineData("1NNSA")]
    [InlineData("IN-SA")]
    public async Task ABadLocation_IsRejected(string location)
    {
        var (service, _) = Create();

        var act = () => service.RunAsync(new UtilisationQuery(Location: location), default);

        (await act.Should().ThrowAsync<DomainValidationException>()).Which.Errors.Should().ContainKey("Location");
    }

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, UtilisationReportService.MaxPageSize + 1, "PageSize")]
    public async Task BadPaging_IsRejected(int page, int pageSize, string field)
    {
        var (service, _) = Create();

        var act = () => service.RunAsync(new UtilisationQuery(Page: page, PageSize: pageSize), default);

        (await act.Should().ThrowAsync<DomainValidationException>()).Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task ThePageSizeLimit_IsAcceptedExactly()
    {
        var (service, _) = Create();

        var result = await service.RunAsync(new UtilisationQuery(PageSize: UtilisationReportService.MaxPageSize), default);

        result.PageSize.Should().Be(200);
    }

    // ---- completing the raw counts ----------------------------------------------------------------

    [Fact]
    public async Task TheRawCountsAreCompletedWithPercentages_AndMonthsAreOldestFirst()
    {
        var raw = new RawUtilisation(
            [new RawUtilisationRow(new DateOnly(2026, 11, 1), "INNSA", "Nhava Sheva", 40, 10, 5, 5)],
            TotalRows: 1,
            FleetSize: 40,
            [new RawMonthly(new DateOnly(2026, 11, 1), 40, 20), new RawMonthly(new DateOnly(2026, 10, 1), 30, 10)]);
        var (service, _) = Create(raw);

        var result = await service.RunAsync(new UtilisationQuery(), default);

        result.Rows.Single().UtilisationPercent.Should().Be(25.0m);
        result.FleetSize.Should().Be(40);
        result.Monthly.Select(m => (m.Month.Month, m.UtilisationPercent)).Should().Equal((10, 25.0m), (11, 50.0m));
    }
}
