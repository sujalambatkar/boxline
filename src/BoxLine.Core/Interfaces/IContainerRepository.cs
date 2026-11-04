using BoxLine.Core.Common;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IContainerRepository
{
    Task<PagedResult<Container>> SearchAsync(ContainerQuery query, CancellationToken cancellationToken);

    Task<Container?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>The container with this normalized ISO 6346 number, including its type, or null.</summary>
    Task<Container?> GetByNumberAsync(string number, CancellationToken cancellationToken);

    /// <summary>True when movement events refer to the container, which then cannot be deleted.</summary>
    Task<bool> HasMovementsAsync(int id, CancellationToken cancellationToken);

    Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken);

    Task AddAsync(Container container, CancellationToken cancellationToken);

    Task UpdateAsync(Container container, CancellationToken cancellationToken);

    Task DeleteAsync(Container container, CancellationToken cancellationToken);
}
