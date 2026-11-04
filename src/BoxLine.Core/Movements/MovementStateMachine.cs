using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Movements;

/// <summary>The latest recorded event of a container, as the state machine needs to see it.</summary>
public sealed record LastMovement(MovementEventType EventType, DateTime OccurredAt);

/// <summary>
/// The container life cycle: gate out empty, stuffed, gate in full, loaded, discharged, gate out full,
/// delivered, empty returned, and then around again. Events must come in that order and never go back in time.
/// </summary>
public static class MovementStateMachine
{
    /// <summary>How far ahead of the server clock an event time may be, to absorb clock differences.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<MovementEventType, MovementEventType[]> Next = new()
    {
        [MovementEventType.GateOutEmpty] = [MovementEventType.Stuffed],
        [MovementEventType.Stuffed] = [MovementEventType.GateInFull],
        [MovementEventType.GateInFull] = [MovementEventType.LoadedOnVessel],
        [MovementEventType.LoadedOnVessel] = [MovementEventType.Discharged],
        [MovementEventType.Discharged] = [MovementEventType.GateOutFull],
        [MovementEventType.GateOutFull] = [MovementEventType.DeliveredToConsignee],
        [MovementEventType.DeliveredToConsignee] = [MovementEventType.EmptyReturned],
        [MovementEventType.EmptyReturned] = [MovementEventType.GateOutEmpty],
    };

    /// <summary>The events that may follow <paramref name="last"/>; for a container with no history, only gate out empty.</summary>
    public static IReadOnlyList<MovementEventType> AllowedNext(MovementEventType? last) =>
        last is null ? [MovementEventType.GateOutEmpty] : Next[last.Value];

    public static bool IsAllowed(MovementEventType? last, MovementEventType next) => AllowedNext(last).Contains(next);

    /// <summary>Checks a new event against the container's latest one.</summary>
    /// <exception cref="ConflictException">The event is out of order.</exception>
    /// <exception cref="DomainValidationException">The event time is before the previous event or in the future.</exception>
    public static void Validate(LastMovement? last, MovementEventType next, DateTime occurredAtUtc, DateTime nowUtc)
    {
        if (!IsAllowed(last?.EventType, next))
        {
            var expected = string.Join(" or ", AllowedNext(last?.EventType));
            var after = last is null ? "a container with no history" : $"{last.EventType}";
            throw new ConflictException($"{next} cannot follow {after}. Expected: {expected}.");
        }

        if (last is not null && occurredAtUtc < last.OccurredAt)
        {
            throw new DomainValidationException(
                "OccurredAt", $"The event time cannot be earlier than the previous event ({last.OccurredAt:yyyy-MM-dd HH:mm} UTC).");
        }

        if (occurredAtUtc > nowUtc + FutureTolerance)
        {
            throw new DomainValidationException("OccurredAt", "The event time cannot be in the future.");
        }
    }
}
