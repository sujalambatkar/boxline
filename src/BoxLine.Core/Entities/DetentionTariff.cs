namespace BoxLine.Core.Entities;

/// <summary>
/// Detention and demurrage charges for one container type: some free days, then a rate per day that steps up
/// in slabs. Slabs are expressed in absolute day numbers (for example days 6 to 10, then 11 to 15, then 16 onwards).
/// </summary>
public class DetentionTariff
{
    public int Id { get; set; }

    public int ContainerTypeId { get; set; }

    public ContainerType? ContainerType { get; set; }

    /// <summary>Days at the start of the period that are not charged.</summary>
    public int FreeDays { get; set; }

    /// <summary>First day (inclusive) the tariff applies to a detention clock that starts on it.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last day (inclusive) the tariff applies; null means it has no end date.</summary>
    public DateOnly? ValidTo { get; set; }

    public List<DetentionSlab> Slabs { get; set; } = [];

    public bool IsValidOn(DateOnly date) => date >= ValidFrom && (ValidTo is null || date <= ValidTo);
}

/// <summary>A band of days charged at one daily rate.</summary>
public class DetentionSlab
{
    public int Id { get; set; }

    public int TariffId { get; set; }

    /// <summary>First day number of the slab (1 is the first day of the period).</summary>
    public int FromDay { get; set; }

    /// <summary>Last day number of the slab; null for the open-ended last slab.</summary>
    public int? ToDay { get; set; }

    public decimal RatePerDayUsd { get; set; }
}
