using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Bookings;

namespace Studium_Booking.Data.Configurations;

public class BookingAuditLogConfig : IEntityTypeConfiguration<BookingAuditLog>
{
    public void Configure(EntityTypeBuilder<BookingAuditLog> entity)
    {
        entity.ToTable("BookingAuditLogs");
        entity.HasKey(a => a.Id);

        entity.Property(a => a.Id).ValueGeneratedNever();
        entity.Property(a => a.BookingId).IsRequired();
        entity.Property(a => a.Action).HasMaxLength(64).IsRequired();
        entity.Property(a => a.PerformedByUserId).HasMaxLength(128).IsRequired();
        entity.Property(a => a.Details).HasMaxLength(1000);
        entity.Property(a => a.TimestampUtc).HasDefaultValueSql("now()").IsRequired();

        entity.HasIndex(a => a.BookingId);
        entity.HasIndex(a => a.TimestampUtc);
        entity.HasIndex(a => a.Action);
    }
}
