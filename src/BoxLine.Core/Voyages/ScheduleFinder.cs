using BoxLine.Core.Entities;

namespace BoxLine.Core.Voyages;

/// <summary>A sailing that connects two ports: the voyage with its call at each end.</summary>
public sealed record ScheduleOption(Voyage Voyage, VoyageCall Pol, VoyageCall Pod);

/// <summary>Finds sailings between two ports whose departure falls inside a date window.</summary>
public static class ScheduleFinder
{
    /// <param name="voyages">Candidates, each with its calls loaded.</param>
    /// <param name="departFrom">First departure day, inclusive (UTC).</param>
    /// <param name="departTo">Last departure day, inclusive (UTC).</param>
    public static IReadOnlyList<ScheduleOption> Find(
        IEnumerable<Voyage> voyages, int polId, int podId, DateOnly departFrom, DateOnly departTo)
    {
        var options = new List<ScheduleOption>();

        foreach (var voyage in voyages)
        {
            if (!VoyageRotation.IsOpenForBooking(voyage, polId) || VoyageRotation.FindLeg(voyage.Calls, polId, podId) is not var (pol, pod))
            {
                continue;
            }

            if (pol.Etd is not { } departure || pod.Eta is null)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(departure);
            if (day >= departFrom && day <= departTo)
            {
                options.Add(new ScheduleOption(voyage, pol, pod));
            }
        }

        return options.OrderBy(o => o.Pol.Etd).ThenBy(o => o.Voyage.Id).ToList();
    }
}
