using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class PortRepository(BoxLineDbContext db) : IPortRepository
{
    public async Task<IReadOnlyList<Port>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var query = db.Ports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.UnLocode.Contains(term));
        }

        return await query.OrderBy(p => p.UnLocode).ToListAsync(cancellationToken);
    }

    public Task<Port?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Ports.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> UnLocodeExistsAsync(string unLocode, int? excludeId, CancellationToken cancellationToken) =>
        db.Ports.AnyAsync(p => p.UnLocode == unLocode && p.Id != excludeId, cancellationToken);

    public async Task AddAsync(Port port, CancellationToken cancellationToken)
    {
        db.Ports.Add(port);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Port port, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(Port port, CancellationToken cancellationToken)
    {
        db.Ports.Remove(port);
        await db.SaveChangesAsync(cancellationToken);
    }
}
