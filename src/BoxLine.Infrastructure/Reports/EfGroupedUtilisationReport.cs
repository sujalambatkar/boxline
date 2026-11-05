using BoxLine.Core.Entities;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Reports;

/// <summary>
/// The middle step between the naive report and the stored procedure: still plain EF Core LINQ, but filtering, grouping,
/// counting and paging run in SQL Server and nothing is tracked. It exists so that <c>docs/PERFORMANCE.md</c> can show how much
/// of the improvement comes simply from not loading everything, and how much from the procedure and the covering index. It is
/// also checked against the naive report in the tests. Not registered for use by the API.
/// </summary>
public sealed class EfGroupedUtilisationReport(BoxLineDbContext db) : IUtilisationReport
{
    public async Task<RawUtilisation> RunAsync(UtilisationQuery query, CancellationToken cancellationToken)
    {
        var movements = db.ContainerMovements.AsNoTracking()
            .Where(m => query.FromMonth == null || m.OccurredMonth >= query.FromMonth)
            .Where(m => query.ToMonth == null || m.OccurredMonth <= query.ToMonth)
            .Where(m => query.Location == null || m.LocationUnLocode == query.Location);

        var grouped = movements
            .GroupBy(m => new { m.OccurredMonth, m.LocationUnLocode })
            .Select(g => new
            {
                g.Key.OccurredMonth,
                g.Key.LocationUnLocode,
                Movements = g.Count(),
                Containers = g.Select(m => m.ContainerId).Distinct().Count(),
                Loaded = g.Count(m => m.EventType == MovementEventType.LoadedOnVessel),
                Discharged = g.Count(m => m.EventType == MovementEventType.Discharged),
            });

        var totalRows = await grouped.CountAsync(cancellationToken);

        var page = await grouped
            .OrderByDescending(r => r.OccurredMonth)
            .ThenBy(r => r.LocationUnLocode)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var locations = page.Select(r => r.LocationUnLocode).Distinct().ToList();
        var names = await db.Ports.AsNoTracking()
            .Where(p => locations.Contains(p.UnLocode))
            .ToDictionaryAsync(p => p.UnLocode, p => p.Name, cancellationToken);

        var monthly = await movements
            .GroupBy(m => m.OccurredMonth)
            .Select(g => new { Month = g.Key, Movements = g.Count(), Active = g.Select(m => m.ContainerId).Distinct().Count() })
            .OrderBy(m => m.Month)
            .ToListAsync(cancellationToken);

        var fleet = await db.Containers.CountAsync(cancellationToken);

        return new RawUtilisation(
            page.Select(r => new RawUtilisationRow(
                r.OccurredMonth, r.LocationUnLocode, names.GetValueOrDefault(r.LocationUnLocode), r.Movements, r.Containers, r.Loaded, r.Discharged)).ToList(),
            totalRows,
            fleet,
            monthly.Select(m => new RawMonthly(m.Month, m.Movements, m.Active)).ToList());
    }
}
