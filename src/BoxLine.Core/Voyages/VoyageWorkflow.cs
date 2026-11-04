using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Voyages;

/// <summary>Voyage lifecycle: Scheduled to Underway to Completed, and a voyage can be cancelled until it completes.</summary>
public static class VoyageWorkflow
{
    private static readonly Dictionary<VoyageStatus, VoyageStatus[]> Transitions = new()
    {
        [VoyageStatus.Scheduled] = [VoyageStatus.Underway, VoyageStatus.Cancelled],
        [VoyageStatus.Underway] = [VoyageStatus.Completed, VoyageStatus.Cancelled],
        [VoyageStatus.Completed] = [],
        [VoyageStatus.Cancelled] = [],
    };

    public static bool CanTransition(VoyageStatus from, VoyageStatus to) => Transitions[from].Contains(to);

    public static IReadOnlyList<VoyageStatus> NextStatuses(VoyageStatus from) => Transitions[from];

    /// <exception cref="ConflictException">The transition is not allowed.</exception>
    public static void EnsureCanTransition(VoyageStatus from, VoyageStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new ConflictException($"A {from} voyage cannot move to {to}.");
        }
    }
}
