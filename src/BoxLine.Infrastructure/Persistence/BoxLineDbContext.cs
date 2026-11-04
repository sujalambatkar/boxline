using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Persistence;

/// <summary>EF Core context for the BoxLine database.</summary>
public class BoxLineDbContext(DbContextOptions<BoxLineDbContext> options) : DbContext(options)
{
    public DbSet<Port> Ports => Set<Port>();

    public DbSet<ContainerType> ContainerTypes => Set<ContainerType>();

    public DbSet<Container> Containers => Set<Container>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingLine> BookingLines => Set<BookingLine>();

    public DbSet<ContainerMovement> ContainerMovements => Set<ContainerMovement>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BoxLineDbContext).Assembly);
    }
}
