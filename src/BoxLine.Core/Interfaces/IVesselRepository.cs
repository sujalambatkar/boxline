using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IVesselRepository
{
    Task<IReadOnlyList<Vessel>> ListAsync(string? search, CancellationToken cancellationToken);

    Task<Vessel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool> ImoExistsAsync(string imoNumber, int? excludeId, CancellationToken cancellationToken);

    Task<bool> HasVoyagesAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(Vessel vessel, CancellationToken cancellationToken);

    Task UpdateAsync(Vessel vessel, CancellationToken cancellationToken);

    Task DeleteAsync(Vessel vessel, CancellationToken cancellationToken);
}
