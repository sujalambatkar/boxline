using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class PortConfiguration : IEntityTypeConfiguration<Port>
{
    public void Configure(EntityTypeBuilder<Port> builder)
    {
        builder.Property(p => p.UnLocode).IsRequired().HasMaxLength(5).IsFixedLength().IsUnicode(false);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Country).IsRequired().HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.HasIndex(p => p.UnLocode).IsUnique();
    }
}
