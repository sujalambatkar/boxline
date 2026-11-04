using BoxLine.Core.Entities;

namespace BoxLine.Core.Detention;

/// <summary>When a container's detention clock started and, if it has come back, stopped.</summary>
/// <param name="StartedBy">The event that started the clock: gate out full, or delivery when no gate out was recorded.</param>
public sealed record DetentionClock(DateOnly Start, DateOnly? End, MovementEventType StartedBy, ContainerMovement StartEvent);

/// <summary>Works out the detention clock from a container's movement history.</summary>
public static class DetentionCycle
{
    /// <summary>
    /// The latest gate out full among <paramref name="candidates"/> (or the latest delivery when there is no gate
    /// out) starts the clock; the first empty-returned event after it in the full history stops it.
    /// </summary>
    /// <param name="history">Every movement of the container, oldest first.</param>
    /// <param name="candidates">The movements allowed to start the clock (for example only one booking's).</param>
    /// <returns>The clock, or null when no gate out or delivery qualifies.</returns>
    public static DetentionClock? Find(IReadOnlyList<ContainerMovement> history, Func<ContainerMovement, bool> candidates)
    {
        var start = history.LastOrDefault(e => e.EventType == MovementEventType.GateOutFull && candidates(e))
            ?? history.LastOrDefault(e => e.EventType == MovementEventType.DeliveredToConsignee && candidates(e));
        if (start is null)
        {
            return null;
        }

        var startIndex = history.ToList().IndexOf(start);
        var end = history.Skip(startIndex + 1).FirstOrDefault(e => e.EventType == MovementEventType.EmptyReturned);

        return new DetentionClock(
            DateOnly.FromDateTime(start.OccurredAt),
            end is null ? null : DateOnly.FromDateTime(end.OccurredAt),
            start.EventType,
            start);
    }
}
