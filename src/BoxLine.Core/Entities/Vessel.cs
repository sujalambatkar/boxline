namespace BoxLine.Core.Entities;

public class Vessel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Seven-digit IMO number (normalized, digits only); unique.</summary>
    public string ImoNumber { get; set; } = string.Empty;
}
