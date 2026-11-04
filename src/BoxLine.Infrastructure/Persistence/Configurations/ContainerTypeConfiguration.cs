using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class ContainerTypeConfiguration : IEntityTypeConfiguration<ContainerType>
{
    public void Configure(EntityTypeBuilder<ContainerType> builder)
    {
        builder.Property(t => t.Code).IsRequired().HasMaxLength(4).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(200);
        builder.HasIndex(t => t.Code).IsUnique();
    }
}
