namespace BoxLine.Core.Ports;

/// <summary>Helpers for UN/LOCODE port codes: two country letters and three location characters.</summary>
public static class UnLocode
{
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>True for five characters where the first two are letters and the last three are letters or digits 2-9.</summary>
    public static bool IsValid(string? value)
    {
        var code = Normalize(value);
        return code.Length == 5
            && code.Take(2).All(c => c is >= 'A' and <= 'Z')
            && code.Skip(2).All(c => c is >= 'A' and <= 'Z' or >= '2' and <= '9');
    }
}
