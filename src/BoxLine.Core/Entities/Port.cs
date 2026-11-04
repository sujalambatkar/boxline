namespace BoxLine.Core.Entities;

/// <summary>A seaport identified by its UN/LOCODE (for example INNSA).</summary>
public class Port
{
    public int Id { get; set; }

    /// <summary>Five-character UN/LOCODE: two-letter country code plus three-character location.</summary>
    public string UnLocode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code; always equal to the first two letters of <see cref="UnLocode"/>.</summary>
    public string Country { get; set; } = string.Empty;
}
