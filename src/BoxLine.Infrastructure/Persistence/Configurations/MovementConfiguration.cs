using BoxLine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoxLine.Infrastructure.Persistence.Configurations;

public sealed class ContainerMovementConfiguration : IEntityTypeConfiguration<ContainerMovement>
{
    public const string ReportIndexName = "IX_ContainerMovements_Report";

    public void Configure(EntityTypeBuilder<ContainerMovement> builder)
    {
        builder.Property(m => m.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.LocationUnLocode).IsRequired().HasMaxLength(5).IsFixedLength().IsUnicode(false);
        builder.Property(m => m.Remarks).HasMaxLength(500);

        // The first day of the movement's month, computed and stored by SQL Server so reports can group and range-filter
        // by month straight from an index instead of evaluating YEAR()/MONTH() on every row.
        builder.Property(m => m.OccurredMonth)
            .HasColumnType("date")
            .HasComputedColumnSql("DATEFROMPARTS(YEAR([OccurredAt]), MONTH([OccurredAt]), 1)", stored: true);

        // A container's timeline is always read oldest-first.
        builder.HasIndex(m => new { m.ContainerId, m.OccurredAt, m.Id });

        // Covers the utilisation report: ordered by (month, location, container) so the counts and the distinct-container
        // counts can be produced by streaming through the index, and carrying the event type so the table is never touched.
        builder.HasIndex(m => new { m.OccurredMonth, m.LocationUnLocode, m.ContainerId })
            .IncludeProperties(m => m.EventType)
            .HasDatabaseName(ReportIndexName);
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
