using BoxLine.Core.Entities;

namespace BoxLine.Core.Detention;

public sealed record SlabDraft(int FromDay, int? ToDay, decimal RatePerDayUsd);

/// <summary>Rules a tariff must satisfy so that every possible number of days has exactly one price.</summary>
public static class DetentionTariffRules
{
    public const int MaxFreeDays = 60;
    public const int MaxSlabs = 10;
    public const decimal MaxRatePerDay = 10_000m;

    /// <summary>Returns problems keyed by field (FreeDays, ValidTo, Slabs); an empty dictionary means the tariff is valid.</summary>
    public static Dictionary<string, string[]> Validate(
        int freeDays, DateOnly validFrom, DateOnly? validTo, IReadOnlyList<SlabDraft> slabs)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        if (freeDays is < 0 or > MaxFreeDays)
        {
            Add("FreeDays", $"Free days must be between 0 and {MaxFreeDays}.");
        }

        if (validTo is { } to && to < validFrom)
        {
            Add("ValidTo", "The end date cannot be before the start date.");
        }

        if (slabs.Count == 0)
        {
            Add("Slabs", "Add at least one slab.");
        }
        else if (slabs.Count > MaxSlabs)
        {
            Add("Slabs", $"A tariff can have at most {MaxSlabs} slabs.");
        }

        var ordered = slabs.OrderBy(s => s.FromDay).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var slab = ordered[i];
            var n = i + 1;
            var isLast = i == ordered.Count - 1;

            if (i == 0 && slab.FromDay != freeDays + 1)
            {
                Add("Slabs", $"The first slab must start on day {freeDays + 1}, right after the free days.");
            }

            if (i > 0)
            {
                var previous = ordered[i - 1];
                if (previous.ToDay is { } previousEnd && slab.FromDay != previousEnd + 1)
                {
                    Add("Slabs", $"Slab {n} must start on day {previousEnd + 1}, right after slab {n - 1} ends.");
                }
            }

            if (isLast)
            {
                if (slab.ToDay is not null)
                {
                    Add("Slabs", "The last slab must be open-ended so that long stays are always priced.");
                }
            }
            else if (slab.ToDay is null)
            {
                Add("Slabs", $"Only the last slab can be open-ended (slab {n} has no end day).");
            }

            if (slab.ToDay is { } end && end < slab.FromDay)
            {
                Add("Slabs", $"Slab {n}: the end day cannot be before the start day.");
            }

            if (slab.RatePerDayUsd <= 0 || slab.RatePerDayUsd > MaxRatePerDay || decimal.Round(slab.RatePerDayUsd, 2) != slab.RatePerDayUsd)
            {
                Add("Slabs", $"Slab {n}: the daily rate must be above 0 and at most {MaxRatePerDay:0} USD, with at most two decimals.");
            }
        }

        foreach (var duplicate in ordered.GroupBy(s => s.FromDay).Where(g => g.Count() > 1))
        {
            Add("Slabs", $"Two slabs start on day {duplicate.Key}.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    public static IReadOnlyList<SlabDraft> ToDrafts(IEnumerable<DetentionSlab> slabs) =>
        slabs.OrderBy(s => s.FromDay).Select(s => new SlabDraft(s.FromDay, s.ToDay, s.RatePerDayUsd)).ToList();
}
