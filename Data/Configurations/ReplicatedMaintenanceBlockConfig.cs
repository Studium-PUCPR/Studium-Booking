using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;

namespace Studium_Booking.Data.Configurations;

public class ReplicatedMaintenanceBlockConfig : IEntityTypeConfiguration<ReplicatedMaintenanceBlock>
{
    public void Configure(EntityTypeBuilder<ReplicatedMaintenanceBlock> entity)
    {
        entity.ToTable("ReplicatedMaintenanceBlocks");
        entity.HasKey(m => m.Id);

        entity.Property(m => m.Id).ValueGeneratedNever();
        entity.Property(m => m.RoomId).IsRequired();
        entity.Property(m => m.StartsAtUtc).IsRequired();
        entity.Property(m => m.EndsAtUtc).IsRequired();
        entity.Property(m => m.Reason).HasMaxLength(500).IsRequired();

        entity.HasIndex(m => m.RoomId);
        entity.HasIndex(m => new { m.RoomId, m.StartsAtUtc, m.EndsAtUtc });
    }
}
