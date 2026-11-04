using BoxLine.Core.Bookings;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class BookingRepository(BoxLineDbContext db) : IBookingRepository
{
    private const string AcquireNumberLockSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = 'BoxLine.BookingNumber',
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 10000;
        IF @result < 0 THROW 50001, 'Could not acquire the booking number lock.', 1;
        """;

    public async Task<PagedResult<Booking>> SearchAsync(BookingQuery query, CancellationToken cancellationToken)
    {
        var bookings = db.Bookings.AsNoTracking();

        if (query.Status is { } status)
        {
            bookings = bookings.Where(b => b.Status == status);
        }

        if (query.PolId is { } polId)
        {
            bookings = bookings.Where(b => b.PolId == polId);
        }

        if (query.PodId is { } podId)
        {
            bookings = bookings.Where(b => b.PodId == podId);
        }

        if (query.ShipperId is { } shipperId)
        {
            bookings = bookings.Where(b => b.ShipperId == shipperId);
        }

        if (query.CreatedFrom is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            bookings = bookings.Where(b => b.CreatedAt >= start);
        }

        if (query.CreatedTo is { } to)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            bookings = bookings.Where(b => b.CreatedAt < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            bookings = bookings.Where(b => b.BookingNumber.Contains(term) || b.CargoDescription.Contains(term));
        }

        var total = await bookings.CountAsync(cancellationToken);

        var items = await bookings
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(b => b.Shipper)
            .Include(b => b.Pol)
            .Include(b => b.Pod)
            .Include(b => b.Voyage).ThenInclude(v => v!.Vessel)
            .Include(b => b.Voyage).ThenInclude(v => v!.Calls)
            .Include(b => b.Lines).ThenInclude(l => l.ContainerType)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<Booking>(items, total, query.Page, query.PageSize);
    }

    public Task<Booking?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Bookings
            .Include(b => b.Shipper)
            .Include(b => b.Pol)
            .Include(b => b.Pod)
            .Include(b => b.Voyage).ThenInclude(v => v!.Vessel)
            .Include(b => b.Voyage).ThenInclude(v => v!.Calls)
            .Include(b => b.Lines).ThenInclude(l => l.ContainerType)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Booking>> ListByVoyageAsync(int voyageId, CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking().Where(b => b.VoyageId == voyageId).OrderBy(b => b.BookingNumber).ToListAsync(cancellationToken);

    public Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken cancellationToken) =>
        db.Bookings
            .AsNoTracking()
            .Include(b => b.Shipper)
            .Include(b => b.Pol)
            .Include(b => b.Pod)
            .FirstOrDefaultAsync(b => b.BookingNumber == bookingNumber, cancellationToken);

    public async Task<int?> GetShipperIdAsync(int bookingId, CancellationToken cancellationToken) =>
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => (int?)b.ShipperId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken)
    {
        var prefix = BookingNumber.Prefix(booking.CreatedAt);

        // Allocating "the next number" is a read-then-write, so concurrent requests would pick the same one.
        // An application lock held until commit serialises just this step; the unique index remains the backstop.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(AcquireNumberLockSql, cancellationToken);

        var last = await db.Bookings
            .Where(b => b.BookingNumber.StartsWith(prefix))
            .OrderByDescending(b => b.BookingNumber.Length)
            .ThenByDescending(b => b.BookingNumber)
            .Select(b => b.BookingNumber)
            .FirstOrDefaultAsync(cancellationToken);

        booking.BookingNumber = BookingNumber.Format(booking.CreatedAt, BookingNumber.NextSequence(last, prefix));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Booking booking, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        if (expectedRowVersion is not null)
        {
            db.Entry(booking).Property(b => b.RowVersion).OriginalValue = expectedRowVersion;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This booking was changed by someone else. Reload it and try again.");
        }
    }
}
