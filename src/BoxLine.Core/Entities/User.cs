namespace BoxLine.Core.Entities;

public enum UserRole
{
    /// <summary>BoxLine operations staff: sees and manages everything.</summary>
    Ops,

    /// <summary>A customer (shipper) who books containers and sees only their own bookings.</summary>
    Shipper,
}

public class User
{
    public int Id { get; set; }

    /// <summary>Lower-cased e-mail address; unique.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    /// <summary>The shipper's company name. Required for shippers, empty for Ops.</summary>
    public string? ShipperCompany { get; set; }

    public DateTime CreatedAt { get; set; }
}
