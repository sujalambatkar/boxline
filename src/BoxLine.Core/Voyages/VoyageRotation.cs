using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Voyages;

/// <summary>A planned call as submitted by a client: where, and when it is expected to arrive and depart.</summary>
public sealed record CallDraft(int Sequence, int PortId, DateTime? Eta, DateTime? Etd);

/// <summary>Rules for a voyage's port rotation, and for matching a booking's route against it.</summary>
public static class VoyageRotation
{
    public const string ErrorKey = "Calls";

    /// <summary>How far ahead of the server clock an actual time may be, to absorb clock differences.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Returns the problems with a planned rotation; an empty dictionary means it is valid.</summary>
    public static Dictionary<string, string[]> ValidatePlan(IReadOnlyList<CallDraft> calls)
    {
        var errors = new List<string>();

        if (calls.Count < 2)
        {
            errors.Add("A rotation needs at least two port calls.");
        }

        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var n = i + 1;

            if (call.Sequence < 1)
            {
                errors.Add($"Call {n}: sequence must be 1 or more.");
            }
            else if (i > 0 && call.Sequence <= calls[i - 1].Sequence)
            {
                errors.Add($"Call {n}: sequence {call.Sequence} must be greater than the previous call's ({calls[i - 1].Sequence}).");
            }

            var first = i == 0;
            var last = i == calls.Count - 1;
            if (first && call.Etd is null)
            {
                errors.Add($"Call {n} (first port): ETD is required.");
            }
            else if (last && call.Eta is null)
            {
                errors.Add($"Call {n} (last port): ETA is required.");
            }
            else if (!first && !last && (call.Eta is null || call.Etd is null))
            {
                errors.Add($"Call {n}: both ETA and ETD are required for an intermediate port.");
            }

            if (call.Eta is { } eta && call.Etd is { } etd && etd < eta)
            {
                errors.Add($"Call {n}: ETD cannot be before ETA.");
            }

            if (i > 0)
            {
                var previousDeparture = calls[i - 1].Etd ?? calls[i - 1].Eta;
                var arrival = call.Eta ?? call.Etd;
                if (previousDeparture is { } departs && arrival is { } arrives && arrives < departs)
                {
                    errors.Add($"Call {n}: arrival is before call {n - 1} departs.");
                }
            }
        }

        foreach (var group in calls.Select((c, i) => (c.PortId, Number: i + 1)).GroupBy(x => x.PortId).Where(g => g.Count() > 1))
        {
            errors.Add($"A port can appear only once in a rotation (calls {string.Join(" and ", group.Select(x => x.Number))}).");
        }

        return errors.Count == 0 ? [] : new Dictionary<string, string[]> { [ErrorKey] = errors.ToArray() };
    }

    /// <summary>
    /// The calls at the loading and discharge ports when the voyage visits the loading port before the
    /// discharge port; otherwise null.
    /// </summary>
    public static (VoyageCall Pol, VoyageCall Pod)? FindLeg(IEnumerable<VoyageCall> calls, int polId, int podId)
    {
        var list = calls.OrderBy(c => c.Sequence).ToList();
        var pol = list.FirstOrDefault(c => c.PortId == polId);
        var pod = list.FirstOrDefault(c => c.PortId == podId);

        return pol is not null && pod is not null && pol.Sequence < pod.Sequence ? (pol, pod) : null;
    }

    /// <summary>True when the voyage can still take bookings from <paramref name="polId"/>.</summary>
    /// <remarks>Scheduled voyages can; underway ones can until they have departed the loading port.</remarks>
    public static bool IsOpenForBooking(Voyage voyage, int polId) => voyage.Status switch
    {
        VoyageStatus.Scheduled => true,
        VoyageStatus.Underway => voyage.Calls.FirstOrDefault(c => c.PortId == polId) is { Atd: null },
        _ => false,
    };

    /// <summary>The estimated departure at the loading port (UTC), or null when unknown.</summary>
    public static DateTime? Departure(IEnumerable<VoyageCall> calls, int polId) =>
        calls.FirstOrDefault(c => c.PortId == polId)?.Etd;

    /// <summary>The estimated arrival at the discharge port (UTC), or null when unknown.</summary>
    public static DateTime? Arrival(IEnumerable<VoyageCall> calls, int podId) =>
        calls.FirstOrDefault(c => c.PortId == podId)?.Eta;

    /// <exception cref="DomainValidationException">The voyage does not sail from the loading to the discharge port in that order.</exception>
    public static void EnsureServes(Voyage voyage, Port pol, Port pod)
    {
        if (FindLeg(voyage.Calls, pol.Id, pod.Id) is null)
        {
            throw new DomainValidationException(
                "VoyageId", $"Voyage {voyage.VoyageNumber} does not sail from {pol.UnLocode} to {pod.UnLocode} in that order.");
        }
    }
}
