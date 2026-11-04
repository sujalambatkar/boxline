using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Core.Voyages;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class VoyageRepository(BoxLineDbContext db) : IVoyageRepository
{
    public async Task<PagedResult<Voyage>> SearchAsync(VoyageQuery query, CancellationToken cancellationToken)
    {
        var voyages = db.Voyages.AsNoTracking();

        if (query.Status is { } status)
        {
            voyages = voyages.Where(v => v.Status == status);
        }

        if (query.VesselId is { } vesselId)
        {
            voyages = voyages.Where(v => v.VesselId == vesselId);
        }

        if (query.PortId is { } portId)
        {
            voyages = voyages.Where(v => v.Calls.Any(c => c.PortId == portId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            voyages = voyages.Where(v => v.VoyageNumber.Contains(term) || v.Vessel!.Name.Contains(term));
        }

        var total = await voyages.CountAsync(cancellationToken);

        var items = await voyages
            .OrderByDescending(v => v.Calls.Min(c => c.Etd))
            .ThenByDescending(v => v.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(v => v.Vessel)
            .Include(v => v.Calls.OrderBy(c => c.Sequence)).ThenInclude(c => c.Port)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<Voyage>(items, total, query.Page, query.PageSize);
    }

    public Task<Voyage?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Voyages
            .Include(v => v.Vessel)
            .Include(v => v.Calls.OrderBy(c => c.Sequence)).ThenInclude(c => c.Port)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<Voyage?> GetReadOnlyAsync(int id, CancellationToken cancellationToken) =>
        db.Voyages
            .AsNoTracking()
            .Include(v => v.Vessel)
            .Include(v => v.Calls.OrderBy(c => c.Sequence)).ThenInclude(c => c.Port)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<bool> NumberExistsAsync(string voyageNumber, int? excludeId, CancellationToken cancellationToken) =>
        db.Voyages.AnyAsync(v => v.VoyageNumber == voyageNumber && v.Id != excludeId, cancellationToken);

    public async Task<IReadOnlyList<Voyage>> ListServingAsync(int polId, int podId, CancellationToken cancellationToken) =>
        await db.Voyages
            .AsNoTracking()
            .Where(v => (v.Status == VoyageStatus.Scheduled || v.Status == VoyageStatus.Underway)
                && v.Calls.Any(c => c.PortId == polId)
                && v.Calls.Any(c => c.PortId == podId))
            .Include(v => v.Vessel)
            .Include(v => v.Calls.OrderBy(c => c.Sequence)).ThenInclude(c => c.Port)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Voyage voyage, CancellationToken cancellationToken)
    {
        db.Voyages.Add(voyage);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Voyage voyage, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task ReplaceRotationAsync(Voyage voyage, IReadOnlyList<VoyageCall> calls, CancellationToken cancellationToken)
    {
        // Sequence numbers and ports are unique per voyage. Deleting the old calls and inserting the new ones in
        // a single save could insert before it deletes and trip those indexes, so do it in two steps, atomically.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.VoyageCalls.RemoveRange(voyage.Calls);
        voyage.Calls.Clear();
        await db.SaveChangesAsync(cancellationToken);

        foreach (var call in calls)
        {
            call.VoyageId = voyage.Id;
            voyage.Calls.Add(call);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Voyage voyage, CancellationToken cancellationToken)
    {
        db.Voyages.Remove(voyage);
        await db.SaveChangesAsync(cancellationToken);
    }
}
