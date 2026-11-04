using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IMovementRepository
{
    /// <summary>
    /// Appends a movement to a container's history. The container's history is locked while
    /// <paramref name="build"/> runs, so the "what happened last" it is given cannot go stale before the insert.
    /// </summary>
    /// <param name="build">Receives the latest existing movement (or null) and returns the movement to insert; may throw to reject.</param>
    Task<ContainerMovement> RecordAsync(
        int containerId, Func<ContainerMovement?, ContainerMovement> build, CancellationToken cancellationToken);

    /// <summary>A container's movements, oldest first. When <paramref name="shipperId"/> is set, only movements of that shipper's bookings.</summary>
    Task<IReadOnlyList<ContainerMovement>> ListForContainerAsync(int containerId, int? shipperId, CancellationToken cancellationToken);

    /// <summary>All movements of a booking, ordered by container then time, including the containers.</summary>
    Task<IReadOnlyList<ContainerMovement>> ListForBookingAsync(int bookingId, CancellationToken cancellationToken);
}
