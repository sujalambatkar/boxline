using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class DetentionTariffConfiguration : IEntityTypeConfiguration<DetentionTariff>
{
    public void Configure(EntityTypeBuilder<DetentionTariff> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_DetentionTariffs_FreeDays", "[FreeDays] >= 0"));

        builder.Property(t => t.ValidFrom).HasColumnType("date");
        builder.Property(t => t.ValidTo).HasColumnType("date");

        builder.HasIndex(t => new { t.ContainerTypeId, t.ValidFrom });

        builder.HasOne(t => t.ContainerType).WithMany().HasForeignKey(t => t.ContainerTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Slabs).WithOne().HasForeignKey(s => s.TariffId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DetentionSlabConfiguration : IEntityTypeConfiguration<DetentionSlab>
{
    public void Configure(EntityTypeBuilder<DetentionSlab> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_DetentionSlabs_Rate", "[RatePerDayUsd] > 0");
            t.HasCheckConstraint("CK_DetentionSlabs_Days", "[FromDay] >= 1 AND ([ToDay] IS NULL OR [ToDay] >= [FromDay])");
        });

        builder.Property(s => s.RatePerDayUsd).HasPrecision(10, 2);
        builder.HasIndex(s => new { s.TariffId, s.FromDay }).IsUnique();
    }
}
