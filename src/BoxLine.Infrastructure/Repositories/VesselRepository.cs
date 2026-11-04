using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class VesselRepository(BoxLineDbContext db) : IVesselRepository
{
    public async Task<IReadOnlyList<Vessel>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var query = db.Vessels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(v => v.Name.Contains(term) || v.ImoNumber.Contains(term));
        }

        return await query.OrderBy(v => v.Name).ToListAsync(cancellationToken);
    }

    public Task<Vessel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Vessels.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<bool> ImoExistsAsync(string imoNumber, int? excludeId, CancellationToken cancellationToken) =>
        db.Vessels.AnyAsync(v => v.ImoNumber == imoNumber && v.Id != excludeId, cancellationToken);

    public Task<bool> HasVoyagesAsync(int id, CancellationToken cancellationToken) =>
        db.Voyages.AnyAsync(v => v.VesselId == id, cancellationToken);

    public async Task AddAsync(Vessel vessel, CancellationToken cancellationToken)
    {
        db.Vessels.Add(vessel);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Vessel vessel, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(Vessel vessel, CancellationToken cancellationToken)
    {
        db.Vessels.Remove(vessel);
        await db.SaveChangesAsync(cancellationToken);
    }
}
