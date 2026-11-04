using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IPortRepository
{
    Task<IReadOnlyList<Port>> ListAsync(string? search, CancellationToken cancellationToken);

    Task<Port?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool> UnLocodeExistsAsync(string unLocode, int? excludeId, CancellationToken cancellationToken);

    Task AddAsync(Port port, CancellationToken cancellationToken);

    Task UpdateAsync(Port port, CancellationToken cancellationToken);

    Task DeleteAsync(Port port, CancellationToken cancellationToken);
}
