using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Persistence;

/// <summary>EF Core context for the BoxLine database.</summary>
public class BoxLineDbContext(DbContextOptions<BoxLineDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BoxLineDbContext).Assembly);
    }
}
