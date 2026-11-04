using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.Property(b => b.BookingNumber).IsRequired().HasMaxLength(20).IsUnicode(false);
        builder.Property(b => b.CargoDescription).IsRequired().HasMaxLength(500);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasIndex(b => b.BookingNumber).IsUnique();
        builder.HasIndex(b => new { b.ShipperId, b.CreatedAt });
        builder.HasIndex(b => new { b.Status, b.CreatedAt });
        builder.HasIndex(b => b.PolId);
        builder.HasIndex(b => b.PodId);

        builder.HasOne(b => b.Shipper).WithMany().HasForeignKey(b => b.ShipperId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Pol).WithMany().HasForeignKey(b => b.PolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Pod).WithMany().HasForeignKey(b => b.PodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BookingId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BookingLineConfiguration : IEntityTypeConfiguration<BookingLine>
{
    public void Configure(EntityTypeBuilder<BookingLine> builder)
    {
        builder.HasOne(l => l.ContainerType).WithMany().HasForeignKey(l => l.ContainerTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
