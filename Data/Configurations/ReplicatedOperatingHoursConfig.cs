using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;

namespace Studium_Booking.Data.Configurations;

public class ReplicatedOperatingHoursConfig : IEntityTypeConfiguration<ReplicatedOperatingHours>
{
    public void Configure(EntityTypeBuilder<ReplicatedOperatingHours> entity)
    {
        entity.ToTable("ReplicatedOperatingHours");
        entity.HasKey(o => o.Id);

        entity.Property(o => o.Id).ValueGeneratedNever();
        entity.Property(o => o.RoomId).IsRequired();
        entity.Property(o => o.DayOfWeek).IsRequired();
        entity.Property(o => o.OpensAt).IsRequired();
        entity.Property(o => o.ClosesAt).IsRequired();

        entity.HasIndex(o => new { o.RoomId, o.DayOfWeek });
    }
}
