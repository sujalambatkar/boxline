using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class MovementRepository(BoxLineDbContext db) : IMovementRepository
{
    public async Task<ContainerMovement> RecordAsync(
        int containerId, Func<ContainerMovement?, ContainerMovement> build, CancellationToken cancellationToken)
    {
        // Two operators could record the next event for the same container at the same moment, and both would
        // be checked against the same "last event". An application lock per container, held until commit,
        // makes check-then-insert atomic. If build throws, the transaction rolls back and the lock is released.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var resource = $"BoxLine.Movement.{containerId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;
            IF @result < 0 THROW 50001, 'Could not acquire the container movement lock.', 1;
            """,
            cancellationToken);

        var last = await db.ContainerMovements
            .AsNoTracking()
            .Where(m => m.ContainerId == containerId)
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var movement = build(last);
        db.ContainerMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return movement;
    }

    public async Task<IReadOnlyList<ContainerMovement>> ListForContainerAsync(
        int containerId, int? shipperId, CancellationToken cancellationToken)
    {
        var movements = db.ContainerMovements.AsNoTracking().Include(m => m.Booking).Where(m => m.ContainerId == containerId);

        if (shipperId is { } id)
        {
            movements = movements.Where(m => m.Booking != null && m.Booking.ShipperId == id);
        }

        return await movements.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContainerMovement>> ListForBookingAsync(int bookingId, CancellationToken cancellationToken) =>
        await db.ContainerMovements
            .AsNoTracking()
            .Include(m => m.Booking)
            .Include(m => m.Container).ThenInclude(c => c!.ContainerType)
            .Where(m => m.BookingId == bookingId)
            .OrderBy(m => m.Container!.Number).ThenBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
}
