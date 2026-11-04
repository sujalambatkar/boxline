namespace BoxLine.Core.Bookings;

public sealed record BookingLineDraft(int ContainerTypeId, int Quantity);

public sealed record BookingDraft(
    int PolId,
    int PodId,
    string? CargoDescription,
    IReadOnlyList<BookingLineDraft> Lines);

/// <summary>Validation rules for the content of a booking.</summary>
public static class BookingRules
{
    public const int MaxQuantityPerLine = 50;
    public const int MaxCargoDescriptionLength = 500;

    /// <summary>Returns field-keyed error messages; an empty dictionary means the draft is valid.</summary>
    public static Dictionary<string, string[]> Validate(BookingDraft draft)
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

        if (draft.PolId == draft.PodId)
        {
            Add("PodId", "Port of discharge must differ from the port of loading.");
        }

        if (string.IsNullOrWhiteSpace(draft.CargoDescription))
        {
            Add("CargoDescription", "Cargo description is required.");
        }
        else if (draft.CargoDescription.Trim().Length > MaxCargoDescriptionLength)
        {
            Add("CargoDescription", $"Cargo description must be at most {MaxCargoDescriptionLength} characters.");
        }

        if (draft.Lines.Count == 0)
        {
            Add("Lines", "Add at least one container line.");
        }

        foreach (var line in draft.Lines.Where(l => l.Quantity < 1 || l.Quantity > MaxQuantityPerLine))
        {
            Add("Lines", $"Quantity must be between 1 and {MaxQuantityPerLine} (got {line.Quantity}).");
        }

        if (draft.Lines.GroupBy(l => l.ContainerTypeId).Any(g => g.Count() > 1))
        {
            Add("Lines", "Each container type can appear only once; combine the quantities into one line.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
