using Microsoft.EntityFrameworkCore;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Penalties;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;
using Studium_Booking.Domain.Entities.Waitlist;

namespace Studium_Booking.Data;

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingAuditLog> BookingAuditLogs => Set<BookingAuditLog>();
    public DbSet<PenaltyRecord> Penalties => Set<PenaltyRecord>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<ReplicatedRoom> ReplicatedRooms => Set<ReplicatedRoom>();
    public DbSet<ReplicatedMaintenanceBlock> ReplicatedMaintenanceBlocks => Set<ReplicatedMaintenanceBlock>();
    public DbSet<ReplicatedOperatingHours> ReplicatedOperatingHours => Set<ReplicatedOperatingHours>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
    }
}
