using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Studium_Booking.Domain.Entities.Inbox;

namespace Studium_Booking.Data.Configurations;

public class InboxMessageConfig : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> entity)
    {
        entity.ToTable("InboxMessages");
        entity.HasKey(m => m.Id);

        entity.Property(m => m.Id)
            .ValueGeneratedNever();

        entity.Property(m => m.HandlerName)
            .IsRequired()
            .HasMaxLength(150);

        entity.Property(m => m.ReceivedAtUtc)
            .IsRequired();

        entity.HasIndex(m => m.ReceivedAtUtc);
    }
}
