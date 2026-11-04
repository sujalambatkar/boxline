using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class ContainerMovementConfiguration : IEntityTypeConfiguration<ContainerMovement>
{
    public void Configure(EntityTypeBuilder<ContainerMovement> builder)
    {
        builder.Property(m => m.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.LocationUnLocode).IsRequired().HasMaxLength(5).IsFixedLength().IsUnicode(false);
        builder.Property(m => m.Remarks).HasMaxLength(500);

        // A container's timeline is always read oldest-first.
        builder.HasIndex(m => new { m.ContainerId, m.OccurredAt, m.Id });
        builder.HasIndex(m => m.BookingId);

        builder.HasOne(m => m.Container).WithMany().HasForeignKey(m => m.ContainerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Booking).WithMany().HasForeignKey(m => m.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.Property(m => m.Text).IsRequired().HasMaxLength(2000);

        builder.HasIndex(m => new { m.BookingId, m.Id });

        builder.HasOne<Booking>().WithMany().HasForeignKey(m => m.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
