using BoxLine.Core.Bookings;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IBookingRepository
{
    /// <summary>Newest first. Results include shipper, ports and lines with container types.</summary>
    Task<PagedResult<Booking>> SearchAsync(BookingQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a tracked booking with its relations, or null.</summary>
    Task<Booking?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Loads a booking and its relations by booking number, or null.</summary>
    Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken cancellationToken);

    /// <summary>The owning shipper's id, or null when the booking does not exist. A cheap access check.</summary>
    Task<int?> GetShipperIdAsync(int bookingId, CancellationToken cancellationToken);

    /// <summary>The bookings linked to a voyage, without their lines (untracked).</summary>
    Task<IReadOnlyList<Booking>> ListByVoyageAsync(int voyageId, CancellationToken cancellationToken);

    /// <summary>Assigns the next booking number for the booking's month and inserts it, retrying on a number clash.</summary>
    Task AddAsync(Booking booking, CancellationToken cancellationToken);

    /// <summary>Saves changes. When <paramref name="expectedRowVersion"/> is given it must match the stored row.</summary>
    /// <exception cref="ConflictException">Someone else changed the booking first.</exception>
    Task UpdateAsync(Booking booking, byte[]? expectedRowVersion, CancellationToken cancellationToken);
}
