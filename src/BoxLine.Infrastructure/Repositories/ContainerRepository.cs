using BoxLine.Core.Common;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class ContainerRepository(BoxLineDbContext db) : IContainerRepository
{
    public async Task<PagedResult<Container>> SearchAsync(ContainerQuery query, CancellationToken cancellationToken)
    {
        var containers = db.Containers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Iso6346Validator.Normalize(query.Search);
            containers = containers.Where(c => c.Number.Contains(term));
        }

        if (query.ContainerTypeId is { } typeId)
        {
            containers = containers.Where(c => c.ContainerTypeId == typeId);
        }

        if (query.Status is { } status)
        {
            containers = containers.Where(c => c.Status == status);
        }

        var total = await containers.CountAsync(cancellationToken);

        var items = await ApplySort(containers, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(c => c.ContainerType)
            .ToListAsync(cancellationToken);

        return new PagedResult<Container>(items, total, query.Page, query.PageSize);
    }

    public Task<Container?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Containers.Include(c => c.ContainerType).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Container?> GetByNumberAsync(string number, CancellationToken cancellationToken) =>
        db.Containers.AsNoTracking().Include(c => c.ContainerType).FirstOrDefaultAsync(c => c.Number == number, cancellationToken);

    public Task<bool> HasMovementsAsync(int id, CancellationToken cancellationToken) =>
        db.ContainerMovements.AnyAsync(m => m.ContainerId == id, cancellationToken);

    public Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken) =>
        db.Containers.AnyAsync(c => c.Number == number, cancellationToken);

    public async Task AddAsync(Container container, CancellationToken cancellationToken)
    {
        db.Containers.Add(container);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Container container, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(Container container, CancellationToken cancellationToken)
    {
        db.Containers.Remove(container);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<Container> ApplySort(IQueryable<Container> containers, ContainerQuery query)
    {
        var descending = query.Descending;
        return query.SortBy switch
        {
            ContainerSortField.Type => descending
                ? containers.OrderByDescending(c => c.ContainerType!.Code).ThenBy(c => c.Number)
                : containers.OrderBy(c => c.ContainerType!.Code).ThenBy(c => c.Number),
            ContainerSortField.Ownership => descending
                ? containers.OrderByDescending(c => c.OwnershipType).ThenBy(c => c.Number)
                : containers.OrderBy(c => c.OwnershipType).ThenBy(c => c.Number),
            ContainerSortField.Status => descending
                ? containers.OrderByDescending(c => c.Status).ThenBy(c => c.Number)
                : containers.OrderBy(c => c.Status).ThenBy(c => c.Number),
            _ => descending
                ? containers.OrderByDescending(c => c.Number)
                : containers.OrderBy(c => c.Number),
        };
    }
}
