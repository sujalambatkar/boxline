namespace BoxLine.Core.Vessels;

/// <summary>
/// IMO ship identification numbers: seven digits where the last is a check digit. Multiply the first six
/// digits by 7, 6, 5, 4, 3, 2; the last digit of that sum must equal the seventh digit.
/// </summary>
public static class ImoNumber
{
    private const int Length = 7;

    /// <summary>Strips an "IMO" prefix, spaces and hyphens: "IMO 9074729" becomes "9074729".</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return cleaned.StartsWith("IMO", StringComparison.Ordinal) ? cleaned[3..] : cleaned;
    }

    public static bool IsValid(string? value) => Validate(value) is null;

    /// <summary>A human-readable reason the number is invalid, or <c>null</c> when it is valid.</summary>
    public static string? Validate(string? value)
    {
        var number = Normalize(value);

        if (number.Length == 0)
        {
            return "IMO number is required.";
        }

        if (number.Length != Length || !number.All(char.IsAsciiDigit))
        {
            return "IMO number must be exactly 7 digits.";
        }

        var expected = ComputeCheckDigit(number[..6]);
        var actual = number[6] - '0';

        return actual == expected
            ? null
            : $"Check digit is wrong: {number[..6]} should end in {expected}, not {actual}.";
    }

    /// <summary>The check digit for the first six digits.</summary>
    /// <exception cref="ArgumentException">The input is not six digits.</exception>
    public static int ComputeCheckDigit(string firstSix)
    {
        var digits = Normalize(firstSix);
        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Expected exactly six digits.", nameof(firstSix));
        }

        var sum = 0;
        for (var i = 0; i < 6; i++)
        {
            sum += (digits[i] - '0') * (7 - i);
        }

        return sum % 10;
    }
}
