using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IContainerTypeRepository
{
    Task<IReadOnlyList<ContainerType>> ListAsync(CancellationToken cancellationToken);

    Task<ContainerType?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken);

    Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(ContainerType type, CancellationToken cancellationToken);

    Task UpdateAsync(ContainerType type, CancellationToken cancellationToken);

    Task DeleteAsync(ContainerType type, CancellationToken cancellationToken);
}
