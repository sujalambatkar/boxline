using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IDetentionTariffRepository
{
    /// <summary>Tariffs with their container type and slabs (ordered), by container type and start date.</summary>
    Task<IReadOnlyList<DetentionTariff>> ListAsync(int? containerTypeId, CancellationToken cancellationToken);

    /// <summary>Loads a tracked tariff with its type and slabs, or null.</summary>
    Task<DetentionTariff?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<DetentionTariff?> GetReadOnlyAsync(int id, CancellationToken cancellationToken);

    /// <summary>The tariff for the container type that is valid on <paramref name="date"/>, or null.</summary>
    Task<DetentionTariff?> FindValidAsync(int containerTypeId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>True when another tariff for the same container type is valid on any day of the given period.</summary>
    Task<bool> OverlapsAsync(int containerTypeId, DateOnly validFrom, DateOnly? validTo, int? excludeId, CancellationToken cancellationToken);

    Task AddAsync(DetentionTariff tariff, CancellationToken cancellationToken);

    Task UpdateAsync(DetentionTariff tariff, CancellationToken cancellationToken);

    /// <summary>Replaces a tariff's slabs (unique per-tariff indexes make this a two-step write).</summary>
    Task ReplaceSlabsAsync(DetentionTariff tariff, IReadOnlyList<DetentionSlab> slabs, CancellationToken cancellationToken);

    Task DeleteAsync(DetentionTariff tariff, CancellationToken cancellationToken);
}
