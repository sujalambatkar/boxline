using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;

namespace BoxLine.Core.Interfaces;

public interface IVoyageRepository
{
    /// <summary>Newest departures first; each voyage includes its vessel and its calls with their ports.</summary>
    Task<PagedResult<Voyage>> SearchAsync(VoyageQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a tracked voyage with its vessel and ordered calls (and their ports), or null.</summary>
    Task<Voyage?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Like <see cref="GetByIdAsync"/> but untracked, for reading only.</summary>
    Task<Voyage?> GetReadOnlyAsync(int id, CancellationToken cancellationToken);

    Task<bool> NumberExistsAsync(string voyageNumber, int? excludeId, CancellationToken cancellationToken);

    /// <summary>Scheduled and underway voyages whose rotation includes both ports (in either order), with calls loaded.</summary>
    Task<IReadOnlyList<Voyage>> ListServingAsync(int polId, int podId, CancellationToken cancellationToken);

    Task AddAsync(Voyage voyage, CancellationToken cancellationToken);

    Task UpdateAsync(Voyage voyage, CancellationToken cancellationToken);

    /// <summary>Replaces a voyage's whole rotation (the unique per-voyage sequence and port indexes make this a two-step write).</summary>
    Task ReplaceRotationAsync(Voyage voyage, IReadOnlyList<VoyageCall> calls, CancellationToken cancellationToken);

    Task DeleteAsync(Voyage voyage, CancellationToken cancellationToken);
}
