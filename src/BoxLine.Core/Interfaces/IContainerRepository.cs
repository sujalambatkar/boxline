using BoxLine.Core.Common;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IContainerRepository
{
    Task<PagedResult<Container>> SearchAsync(ContainerQuery query, CancellationToken cancellationToken);

    Task<Container?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken);

    Task AddAsync(Container container, CancellationToken cancellationToken);

    Task UpdateAsync(Container container, CancellationToken cancellationToken);

    Task DeleteAsync(Container container, CancellationToken cancellationToken);
}
