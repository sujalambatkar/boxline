using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class ContainerConfiguration : IEntityTypeConfiguration<Container>
{
    public void Configure(EntityTypeBuilder<Container> builder)
    {
        builder.Property(c => c.Number).IsRequired().HasMaxLength(11).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.OwnershipType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => c.Number).IsUnique();
        builder.HasIndex(c => c.ContainerTypeId);

        builder.HasOne(c => c.ContainerType)
            .WithMany()
            .HasForeignKey(c => c.ContainerTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
