using BoxLine.Core.Entities;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Reports;

/// <summary>
/// The utilisation report written the way it is easiest to write, and deliberately not the way it should be run.
/// </summary>
/// <remarks>
/// It loads every movement (with its container, tracked) into memory, ignores the filter until the data has arrived,
/// groups with LINQ to Objects and pages at the very end. It is kept for two reasons: it is the baseline that
/// <c>docs/PERFORMANCE.md</c> measures the optimised version against, and it is the oracle the tests use to prove the
/// optimised SQL returns exactly the same numbers. Do not register it for use by the API.
/// </remarks>
public sealed class NaiveUtilisationReport(BoxLineDbContext db) : IUtilisationReport
{
    public async Task<RawUtilisation> RunAsync(UtilisationQuery query, CancellationToken cancellationToken)
    {
        // Everything, with a join to containers nobody asked for, tracked by the change tracker.
        var movements = await db.ContainerMovements.Include(m => m.Container).ToListAsync(cancellationToken);
        var ports = await db.Ports.ToListAsync(cancellationToken);
        var fleetSize = (await db.Containers.ToListAsync(cancellationToken)).Count;

        var inRange = movements
            .Where(m => query.FromMonth is null || Month(m) >= query.FromMonth)
            .Where(m => query.ToMonth is null || Month(m) <= query.ToMonth)
            .Where(m => query.Location is null || m.LocationUnLocode == query.Location)
            .ToList();

        var grouped = inRange
            .GroupBy(m => (Month: Month(m), Location: m.LocationUnLocode))
            .Select(g => new RawUtilisationRow(
                g.Key.Month,
                g.Key.Location,
                ports.FirstOrDefault(p => p.UnLocode == g.Key.Location)?.Name,
                g.Count(),
                g.Select(m => m.ContainerId).Distinct().Count(),
                g.Count(m => m.EventType == MovementEventType.LoadedOnVessel),
                g.Count(m => m.EventType == MovementEventType.Discharged)))
            .OrderByDescending(r => r.Month)
            .ThenBy(r => r.Location, StringComparer.Ordinal)
            .ToList();

        var monthly = inRange
            .GroupBy(Month)
            .Select(g => new RawMonthly(g.Key, g.Count(), g.Select(m => m.ContainerId).Distinct().Count()))
            .OrderBy(m => m.Month)
            .ToList();

        return new RawUtilisation(
            grouped.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            grouped.Count,
            fleetSize,
            monthly);
    }

    private static DateOnly Month(ContainerMovement movement) => new(movement.OccurredAt.Year, movement.OccurredAt.Month, 1);
}
