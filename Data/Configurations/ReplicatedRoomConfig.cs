using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;

namespace Studium_Booking.Data.Configurations;

public class ReplicatedRoomConfig : IEntityTypeConfiguration<ReplicatedRoom>
{
    public void Configure(EntityTypeBuilder<ReplicatedRoom> entity)
    {
        entity.ToTable("ReplicatedRooms");
        entity.HasKey(r => r.Id);

        entity.Property(r => r.Id).ValueGeneratedNever();
        entity.Property(r => r.Code).HasMaxLength(20).IsRequired();
        entity.Property(r => r.Name).HasMaxLength(150).IsRequired();
        entity.Property(r => r.IsActive).IsRequired();
        entity.Property(r => r.LastSyncedAtUtc).IsRequired();

        entity.HasMany(r => r.MaintenanceBlocks)
              .WithOne()
              .HasForeignKey(m => m.RoomId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(r => r.OperatingHours)
              .WithOne()
              .HasForeignKey(o => o.RoomId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(r => r.Code);
        entity.HasIndex(r => r.IsActive);
    }
}
