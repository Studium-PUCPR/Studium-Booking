using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Users;

namespace Studium_Booking.Data.Configurations;

public class BlockedUserConfig : IEntityTypeConfiguration<BlockedUser>
{
    public void Configure(EntityTypeBuilder<BlockedUser> entity)
    {
        entity.ToTable("BlockedUsers");
        entity.HasKey(u => u.UserId);

        entity.Property(u => u.UserId).HasMaxLength(128);
        entity.Property(u => u.BlockReason).HasMaxLength(500);
        entity.Property(u => u.IsBlocked).IsRequired();
        entity.Property(u => u.UpdatedAtUtc).IsRequired();

        entity.HasIndex(u => u.IsBlocked);
    }
}
