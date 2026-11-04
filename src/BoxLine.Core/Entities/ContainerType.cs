namespace BoxLine.Core.Entities;

/// <summary>A container size/type code such as 20GP, 40GP or 40HC.</summary>
public class ContainerType
{
    public int Id { get; set; }

    /// <summary>Size and type code: two digits for the length class followed by two letters (for example 40HC).</summary>
    public string Code { get; set; } = string.Empty;

    public int LengthFt { get; set; }

    public string Description { get; set; } = string.Empty;
}
