using BoxLine.Core.Common;
using BoxLine.Core.Interfaces;

namespace BoxLine.Core.Voyages;

public sealed record ScheduleQuery(int PolId, int PodId, DateOnly? DepartFrom = null, DateOnly? DepartTo = null);

/// <summary>Sailing search for shippers: which voyages connect two ports in a departure window.</summary>
public sealed class ScheduleService(IVoyageRepository voyages, IPortRepository ports, TimeProvider clock)
{
    public const int DefaultWindowDays = 90;
    public const int MaxWindowDays = 366;

    /// <summary>Open sailings from the loading to the discharge port, earliest departure first.</summary>
    public async Task<IReadOnlyList<ScheduleOption>> SearchAsync(ScheduleQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = query.DepartFrom ?? today;
        var to = query.DepartTo ?? from.AddDays(DefaultWindowDays);

        var errors = new Dictionary<string, string[]>();
        if (query.PolId == query.PodId)
        {
            errors["PodId"] = ["Port of discharge must differ from the port of loading."];
        }

        if (to < from)
        {
            errors["DepartTo"] = ["The end of the window cannot be before its start."];
        }
        else if (to.DayNumber - from.DayNumber > MaxWindowDays)
        {
            errors["DepartTo"] = [$"The departure window can span at most {MaxWindowDays} days."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        if (await ports.GetByIdAsync(query.PolId, cancellationToken) is null)
        {
            errors["PolId"] = ["Port of loading does not exist."];
        }

        if (await ports.GetByIdAsync(query.PodId, cancellationToken) is null)
        {
            errors["PodId"] = ["Port of discharge does not exist."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var candidates = await voyages.ListServingAsync(query.PolId, query.PodId, cancellationToken);
        return ScheduleFinder.Find(candidates, query.PolId, query.PodId, from, to);
    }
}
