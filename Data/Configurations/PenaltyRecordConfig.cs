using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Penalties;

namespace Studium_Booking.Data.Configurations;

public class PenaltyRecordConfig : IEntityTypeConfiguration<PenaltyRecord>
{
    public void Configure(EntityTypeBuilder<PenaltyRecord> entity)
    {
        entity.ToTable("PenaltyRecords");
        entity.HasKey(p => p.Id);

        entity.Property(p => p.Id).ValueGeneratedNever();
        entity.Property(p => p.UserId).HasMaxLength(128).IsRequired();
        entity.Property(p => p.Type).IsRequired();
        entity.Property(p => p.Reason).HasMaxLength(500).IsRequired();
        entity.Property(p => p.IssuedAtUtc).HasDefaultValueSql("now()").IsRequired();
        entity.Property(p => p.IsActive).IsRequired();
        entity.Property(p => p.RevokedReason).HasMaxLength(500);

        entity.HasIndex(p => p.UserId);
        entity.HasIndex(p => p.IsActive);
        entity.HasIndex(p => p.Type);
    }
}
