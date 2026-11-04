using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class DetentionTariffRepository(BoxLineDbContext db) : IDetentionTariffRepository
{
    public async Task<IReadOnlyList<DetentionTariff>> ListAsync(int? containerTypeId, CancellationToken cancellationToken)
    {
        var query = db.DetentionTariffs.AsNoTracking();
        if (containerTypeId is { } typeId)
        {
            query = query.Where(t => t.ContainerTypeId == typeId);
        }

        return await query
            .Include(t => t.ContainerType)
            .Include(t => t.Slabs.OrderBy(s => s.FromDay))
            .OrderBy(t => t.ContainerType!.LengthFt).ThenBy(t => t.ContainerType!.Code).ThenBy(t => t.ValidFrom)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public Task<DetentionTariff?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.DetentionTariffs
            .Include(t => t.ContainerType)
            .Include(t => t.Slabs.OrderBy(s => s.FromDay))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<DetentionTariff?> GetReadOnlyAsync(int id, CancellationToken cancellationToken) =>
        db.DetentionTariffs
            .AsNoTracking()
            .Include(t => t.ContainerType)
            .Include(t => t.Slabs.OrderBy(s => s.FromDay))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<DetentionTariff?> FindValidAsync(int containerTypeId, DateOnly date, CancellationToken cancellationToken) =>
        db.DetentionTariffs
            .AsNoTracking()
            .Include(t => t.ContainerType)
            .Include(t => t.Slabs.OrderBy(s => s.FromDay))
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                t => t.ContainerTypeId == containerTypeId && t.ValidFrom <= date && (t.ValidTo == null || t.ValidTo >= date),
                cancellationToken);

    public Task<bool> OverlapsAsync(int containerTypeId, DateOnly validFrom, DateOnly? validTo, int? excludeId, CancellationToken cancellationToken) =>
        db.DetentionTariffs.AnyAsync(
            t => t.ContainerTypeId == containerTypeId
                && t.Id != excludeId
                && (validTo == null || t.ValidFrom <= validTo)
                && (t.ValidTo == null || t.ValidTo >= validFrom),
            cancellationToken);

    public async Task AddAsync(DetentionTariff tariff, CancellationToken cancellationToken)
    {
        db.DetentionTariffs.Add(tariff);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(DetentionTariff tariff, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task ReplaceSlabsAsync(DetentionTariff tariff, IReadOnlyList<DetentionSlab> slabs, CancellationToken cancellationToken)
    {
        // Slabs are unique per (tariff, first day). Deleting the old ones and inserting the new ones in a single
        // save could insert first and trip that index, so do it in two steps inside one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.DetentionSlabs.RemoveRange(tariff.Slabs);
        tariff.Slabs.Clear();
        await db.SaveChangesAsync(cancellationToken);

        foreach (var slab in slabs)
        {
            slab.TariffId = tariff.Id;
            tariff.Slabs.Add(slab);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(DetentionTariff tariff, CancellationToken cancellationToken)
    {
        db.DetentionTariffs.Remove(tariff);
        await db.SaveChangesAsync(cancellationToken);
    }
}
