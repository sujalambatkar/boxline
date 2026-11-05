using BoxLine.Core.Common;
using BoxLine.Core.Ports;

namespace BoxLine.Core.Reports;

/// <summary>Which months, which location and which page of the utilisation report to produce.</summary>
/// <param name="FromMonth">First month (the first day of it), inclusive; null for no lower limit.</param>
/// <param name="ToMonth">Last month (the first day of it), inclusive; null for no upper limit.</param>
/// <param name="Location">Only this UN/LOCODE; null for every location.</param>
public sealed record UtilisationQuery(
    DateOnly? FromMonth = null,
    DateOnly? ToMonth = null,
    string? Location = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>One month at one location.</summary>
/// <param name="PortName">The port's name when the location is a known port; null for inland depots and unknown codes.</param>
/// <param name="Movements">Movement events recorded there that month.</param>
/// <param name="Containers">Different containers that had at least one movement there that month.</param>
/// <param name="Loaded">Containers loaded on a vessel (load events).</param>
/// <param name="Discharged">Containers discharged from a vessel (discharge events).</param>
/// <param name="UtilisationPercent"><see cref="Containers"/> as a percentage of the whole fleet, to one decimal.</param>
public sealed record UtilisationRow(
    DateOnly Month,
    string Location,
    string? PortName,
    int Movements,
    int Containers,
    int Loaded,
    int Discharged,
    decimal UtilisationPercent);

/// <summary>One month across all locations (for the chart).</summary>
/// <param name="ActiveContainers">Different containers that moved anywhere that month.</param>
public sealed record MonthlyUtilisation(DateOnly Month, int Movements, int ActiveContainers, decimal UtilisationPercent);

/// <param name="TotalRows">Rows in the whole report (all pages) for this filter.</param>
/// <param name="FleetSize">Containers in the master list; the denominator of every utilisation percentage.</param>
/// <param name="Monthly">Totals per month for the filter, oldest first, across all pages.</param>
public sealed record UtilisationResult(
    IReadOnlyList<UtilisationRow> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    int FleetSize,
    IReadOnlyList<MonthlyUtilisation> Monthly);

/// <summary>The raw counts both implementations produce before the percentage is worked out.</summary>
public sealed record RawUtilisationRow(DateOnly Month, string Location, string? PortName, int Movements, int Containers, int Loaded, int Discharged);

public sealed record RawMonthly(DateOnly Month, int Movements, int ActiveContainers);

public sealed record RawUtilisation(IReadOnlyList<RawUtilisationRow> Rows, int TotalRows, int FleetSize, IReadOnlyList<RawMonthly> Monthly);

/// <summary>Produces the report. Implemented twice: a deliberately naive version and an optimised SQL one.</summary>
public interface IUtilisationReport
{
    /// <summary>Raw counts for the filter. Rows are ordered newest month first, then by location, and paged.</summary>
    Task<RawUtilisation> RunAsync(UtilisationQuery query, CancellationToken cancellationToken);
}

public static class Utilisation
{
    /// <summary>The share of the fleet as a percentage with one decimal; zero when the fleet is empty.</summary>
    public static decimal Percent(int containers, int fleetSize) =>
        fleetSize <= 0 ? 0m : Math.Round(containers * 100m / fleetSize, 1, MidpointRounding.AwayFromZero);

    public static UtilisationResult Complete(RawUtilisation raw, UtilisationQuery query) => new(
        raw.Rows.Select(r => new UtilisationRow(
            r.Month, r.Location, r.PortName, r.Movements, r.Containers, r.Loaded, r.Discharged, Percent(r.Containers, raw.FleetSize))).ToList(),
        raw.TotalRows,
        query.Page,
        query.PageSize,
        raw.FleetSize,
        raw.Monthly.OrderBy(m => m.Month).Select(m => new MonthlyUtilisation(m.Month, m.Movements, m.ActiveContainers, Percent(m.ActiveContainers, raw.FleetSize))).ToList());
}

/// <summary>Validates a request, applies the defaults and completes the raw counts into a result.</summary>
public sealed class UtilisationReportService(IUtilisationReport report, TimeProvider clock)
{
    public const int MaxPageSize = 200;
    public const int DefaultMonths = 12;

    /// <summary>
    /// Runs the report. Without a month range it covers the latest twelve months, so the default request stays quick
    /// however much history there is.
    /// </summary>
    /// <exception cref="DomainValidationException">The range, location or paging is invalid.</exception>
    public async Task<UtilisationResult> RunAsync(UtilisationQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var from = request.FromMonth is { } f ? FirstOfMonth(f) : (DateOnly?)null;
        var to = request.ToMonth is { } t ? FirstOfMonth(t) : (DateOnly?)null;
        if (from is null && to is null)
        {
            var thisMonth = FirstOfMonth(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            to = thisMonth;
            from = thisMonth.AddMonths(-(DefaultMonths - 1));
        }

        if (from is { } start && to is { } end && end < start)
        {
            errors["ToMonth"] = ["The last month cannot be before the first month."];
        }

        string? location = null;
        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            location = UnLocode.Normalize(request.Location);
            if (!UnLocode.IsValid(location))
            {
                errors["Location"] = ["Location must be a UN/LOCODE: two letters followed by three letters or digits 2-9."];
            }
        }

        if (request.Page < 1)
        {
            errors["Page"] = ["Page must be 1 or more."];
        }

        if (request.PageSize is < 1 or > MaxPageSize)
        {
            errors["PageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var query = new UtilisationQuery(from, to, location, request.Page, request.PageSize);
        return Utilisation.Complete(await report.RunAsync(query, cancellationToken), query);
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
