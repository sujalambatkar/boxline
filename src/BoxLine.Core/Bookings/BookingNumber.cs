namespace BoxLine.Core.Bookings;

/// <summary>Booking references look like BL-202611-0001: month of creation plus a per-month sequence.</summary>
public static class BookingNumber
{
    public static string Prefix(DateTime createdAtUtc) => $"BL-{createdAtUtc:yyyyMM}-";

    public static string Format(DateTime createdAtUtc, int sequence) => $"{Prefix(createdAtUtc)}{sequence:D4}";

    /// <summary>The sequence to use after <paramref name="lastNumber"/> (the highest existing number for the prefix), or 1.</summary>
    public static int NextSequence(string? lastNumber, string prefix)
    {
        if (lastNumber is not null
            && lastNumber.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(lastNumber.AsSpan(prefix.Length), out var last))
        {
            return last + 1;
        }

        return 1;
    }
}
