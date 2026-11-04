using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class VesselConfiguration : IEntityTypeConfiguration<Vessel>
{
    public void Configure(EntityTypeBuilder<Vessel> builder)
    {
        builder.Property(v => v.Name).IsRequired().HasMaxLength(100);
        builder.Property(v => v.ImoNumber).IsRequired().HasMaxLength(7).IsFixedLength().IsUnicode(false);
        builder.HasIndex(v => v.ImoNumber).IsUnique();
    }
}

public sealed class VoyageConfiguration : IEntityTypeConfiguration<Voyage>
{
    public void Configure(EntityTypeBuilder<Voyage> builder)
    {
        builder.Property(v => v.VoyageNumber).IsRequired().HasMaxLength(20).IsUnicode(false);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(v => v.VoyageNumber).IsUnique();
        builder.HasIndex(v => new { v.Status, v.Id });

        builder.HasOne(v => v.Vessel).WithMany().HasForeignKey(v => v.VesselId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(v => v.Calls).WithOne().HasForeignKey(c => c.VoyageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class VoyageCallConfiguration : IEntityTypeConfiguration<VoyageCall>
{
    public void Configure(EntityTypeBuilder<VoyageCall> builder)
    {
        // The database backs up the rotation rules: one call per sequence number and one per port.
        builder.HasIndex(c => new { c.VoyageId, c.Sequence }).IsUnique();
        builder.HasIndex(c => new { c.VoyageId, c.PortId }).IsUnique();
        builder.HasIndex(c => c.PortId);

        builder.HasOne(c => c.Port).WithMany().HasForeignKey(c => c.PortId).OnDelete(DeleteBehavior.Restrict);
    }
}
