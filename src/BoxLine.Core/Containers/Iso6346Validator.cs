namespace BoxLine.Core.Containers;

/// <summary>
/// Validates and generates ISO 6346 container numbers: a three-letter owner code, a category letter
/// (U, J or Z), a six-digit serial and a check digit.
/// </summary>
public static class Iso6346Validator
{
    private const int NumberLength = 11;
    private const int BodyLength = 10;

    private static readonly int[] LetterValues = BuildLetterValues();

    /// <summary>
    /// Upper-cases the input and strips whitespace, hyphens and dots, so "msku 123456-5" becomes "MSKU1234565".
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    /// <summary>True when the (normalized) input is a well-formed ISO 6346 number with a correct check digit.</summary>
    public static bool IsValid(string? value) => Validate(value) is null;

    /// <summary>Returns a human-readable reason the number is invalid, or <c>null</c> when it is valid.</summary>
    public static string? Validate(string? value)
    {
        var number = Normalize(value);

        if (number.Length == 0)
        {
            return "Container number is required.";
        }

        if (number.Length != NumberLength)
        {
            return "Container number must be 11 characters: 3 owner letters, a category letter, 6 digits and a check digit.";
        }

        if (!number.Take(4).All(IsAsciiLetter))
        {
            return "The first four characters must be letters (three-letter owner code plus the category letter).";
        }

        if (number[3] is not ('U' or 'J' or 'Z'))
        {
            return "The fourth character must be the category letter U, J or Z.";
        }

        if (!number.Skip(4).All(char.IsAsciiDigit))
        {
            return "Characters 5 to 11 must be digits (six-digit serial plus the check digit).";
        }

        var body = number[..BodyLength];
        var expected = ComputeCheckDigit(body);
        var actual = number[BodyLength] - '0';

        return actual == expected
            ? null
            : $"Check digit is wrong: {body} should end in {expected}, not {actual}.";
    }

    /// <summary>
    /// Computes the ISO 6346 check digit for the first ten characters: each character is mapped to a value
    /// (digits as themselves, letters A=10 upwards skipping multiples of 11), multiplied by 2^position,
    /// summed, taken modulo 11, and a remainder of 10 becomes 0.
    /// </summary>
    /// <param name="firstTen">Owner code, category letter and six-digit serial.</param>
    /// <exception cref="ArgumentException">The input is not ten characters of 4 letters followed by 6 digits.</exception>
    public static int ComputeCheckDigit(string firstTen)
    {
        var body = Normalize(firstTen);
        if (body.Length != BodyLength
            || !body.Take(4).All(IsAsciiLetter)
            || !body.Skip(4).All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Expected ten characters: four letters followed by six digits.", nameof(firstTen));
        }

        var sum = 0;
        for (var position = 0; position < BodyLength; position++)
        {
            var c = body[position];
            var value = char.IsAsciiDigit(c) ? c - '0' : LetterValues[c - 'A'];
            sum += value << position;
        }

        return sum % 11 % 10;
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z';

    private static int[] BuildLetterValues()
    {
        var values = new int[26];
        var next = 10;
        for (var i = 0; i < values.Length; i++)
        {
            if (next % 11 == 0)
            {
                next++;
            }

            values[i] = next++;
        }

        return values;
    }
}
