using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class ContainerTypeRepository(BoxLineDbContext db) : IContainerTypeRepository
{
    public async Task<IReadOnlyList<ContainerType>> ListAsync(CancellationToken cancellationToken) =>
        await db.ContainerTypes.AsNoTracking().OrderBy(t => t.LengthFt).ThenBy(t => t.Code).ToListAsync(cancellationToken);

    public Task<ContainerType?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.ContainerTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken) =>
        db.ContainerTypes.AnyAsync(t => t.Code == code && t.Id != excludeId, cancellationToken);

    public Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken) =>
        db.Containers.AnyAsync(c => c.ContainerTypeId == id, cancellationToken);

    public async Task AddAsync(ContainerType type, CancellationToken cancellationToken)
    {
        db.ContainerTypes.Add(type);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(ContainerType type, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(ContainerType type, CancellationToken cancellationToken)
    {
        db.ContainerTypes.Remove(type);
        await db.SaveChangesAsync(cancellationToken);
    }
}
