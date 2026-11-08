using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Waitlist;

namespace Studium_Booking.Data.Configurations;

public class WaitlistEntryConfig : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> entity)
    {
        entity.ToTable("WaitlistEntries");
        entity.HasKey(w => w.Id);

        entity.Property(w => w.Id).ValueGeneratedNever();
        entity.Property(w => w.RoomId).IsRequired();
        entity.Property(w => w.UserId).HasMaxLength(128).IsRequired();
        entity.Property(w => w.Status).IsRequired();
        entity.Property(w => w.CreatedAtUtc).HasDefaultValueSql("now()").IsRequired();

        entity.ComplexProperty(w => w.TimeSlot, ts =>
        {
            ts.Property(p => p.StartsAtUtc).HasColumnName("StartsAtUtc").IsRequired();
            ts.Property(p => p.EndsAtUtc).HasColumnName("EndsAtUtc").IsRequired();
        });

        entity.HasIndex(w => w.RoomId);
        entity.HasIndex(w => w.UserId);
        entity.HasIndex(w => w.Status);
    }
}
