using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Bookings;

namespace Studium_Booking.Data.Configurations;

public class BookingConfig : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> entity)
    {
        entity.ToTable("Bookings");
        entity.HasKey(b => b.Id);

        entity.Property(b => b.Id).ValueGeneratedNever();
        entity.Property(b => b.RoomId).IsRequired();
        entity.Property(b => b.UserId).HasMaxLength(128).IsRequired();
        entity.Property(b => b.Title).HasMaxLength(200).IsRequired();
        entity.Property(b => b.Purpose).HasMaxLength(1000);
        entity.Property(b => b.Status).IsRequired();
        entity.Property(b => b.CheckInCode).HasMaxLength(32).IsRequired();
        entity.Property(b => b.CancellationReason).HasMaxLength(500);
        entity.Property(b => b.CreatedAtUtc).HasDefaultValueSql("now()").IsRequired();
        entity.Property(b => b.UpdatedAtUtc);

        entity.ComplexProperty(b => b.TimeSlot, ts =>
        {
            ts.Property(p => p.StartsAtUtc).HasColumnName("StartsAtUtc").IsRequired();
            ts.Property(p => p.EndsAtUtc).HasColumnName("EndsAtUtc").IsRequired();
        });

        entity.HasMany(b => b.AuditLogs)
              .WithOne()
              .HasForeignKey(a => a.BookingId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(b => b.RoomId);
        entity.HasIndex(b => b.UserId);
        entity.HasIndex(b => b.Status);
        entity.HasIndex(b => b.CheckInCode);
        entity.HasIndex(b => new { b.RoomId, b.Status });
    }
}
