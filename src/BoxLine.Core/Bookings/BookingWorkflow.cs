using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Bookings;

/// <summary>The booking status workflow: Draft to Confirmed to Cancelled. Nothing else is allowed.</summary>
public static class BookingWorkflow
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Transitions = new()
    {
        [BookingStatus.Draft] = [BookingStatus.Confirmed],
        [BookingStatus.Confirmed] = [BookingStatus.Cancelled],
        [BookingStatus.Cancelled] = [],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) => Transitions[from].Contains(to);

    public static IReadOnlyList<BookingStatus> NextStatuses(BookingStatus from) => Transitions[from];

    /// <exception cref="ConflictException">The transition is not allowed.</exception>
    public static void EnsureCanTransition(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new ConflictException($"A {from} booking cannot move to {to}.");
        }
    }
}
