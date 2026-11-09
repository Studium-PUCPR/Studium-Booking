using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Inbox;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;
using Studium_Booking.Services.Clients;
using Studium_Booking.Services.DTOs;
using Wolverine;

namespace Studium_Booking.Services.Handlers;

/// <summary>
/// Subscribes to catalog room state, maintenance blocks, and operating hours to synchronize local reservation availability.
/// </summary>
public class SpaceAvailabilitySyncHandler(
    BookingDbContext db,
    IMessageBus messageBus,
    INotificationsClient notificationsClient,
    ILogger<SpaceAvailabilitySyncHandler> logger)
{
    private static DateTime _lastPrunedUtc = DateTime.MinValue;
    private static readonly object _pruneLock = new();

    private async Task<bool> IsDuplicateEventAsync(Guid? eventId, CancellationToken ct)
    {
        if (!eventId.HasValue) return false;
        var exists = await db.InboxMessages.AnyAsync(m => m.Id == eventId.Value, ct);
        if (exists)
        {
            logger.LogWarning("Inbox: event {EventId} already processed in SpaceAvailabilitySyncHandler. Skipping.", eventId.Value);
        }
        return exists;
    }

    private void RecordInboxMessage(Guid? eventId)
    {
        if (eventId.HasValue)
        {
            db.InboxMessages.Add(new InboxMessage(eventId.Value, nameof(SpaceAvailabilitySyncHandler), DateTime.UtcNow));
        }
    }

    private void TriggerThrottledLazyPruning()
    {
        if (DateTime.UtcNow - _lastPrunedUtc > TimeSpan.FromHours(6))
        {
            lock (_pruneLock)
            {
                if (DateTime.UtcNow - _lastPrunedUtc > TimeSpan.FromHours(6))
                {
                    _lastPrunedUtc = DateTime.UtcNow;
                    var cutoff = DateTime.UtcNow.AddDays(-7);
                    _ = db.InboxMessages
                        .Where(m => m.ReceivedAtUtc < cutoff)
                        .ExecuteDeleteAsync(CancellationToken.None);
                }
            }
        }
    }

    public async Task HandleAsync(RoomStatusChangedEvent message, CancellationToken ct = default)
    {
        if (await IsDuplicateEventAsync(message.EventId, ct)) return;

        var room = await db.ReplicatedRooms
            .FirstOrDefaultAsync(r => r.Id == message.RoomId, ct);

        if (room is null)
        {
            room = ReplicatedRoom.Create(
                message.RoomId,
                $"ROOM-{message.RoomId.ToString()[..6].ToUpper()}",
                "Replicated Room",
                message.IsActive,
                message.OccurredAtUtc);
            await db.ReplicatedRooms.AddAsync(room, ct);
        }
        else
        {
            room.UpdateStatus(message.IsActive, message.OccurredAtUtc);
        }

        RecordInboxMessage(message.EventId);
        await db.SaveChangesAsync(ct);
        TriggerThrottledLazyPruning();

        logger.LogInformation("Replicated room {RoomId} status updated to IsActive={IsActive}.",
            message.RoomId, message.IsActive);
    }

    public async Task HandleAsync(RoomMaintenanceEvent message, CancellationToken ct = default)
    {
        if (await IsDuplicateEventAsync(message.EventId, ct)) return;

        var room = await db.ReplicatedRooms
            .Include(r => r.MaintenanceBlocks)
            .FirstOrDefaultAsync(r => r.Id == message.RoomId, ct);

        if (room is null)
        {
            room = ReplicatedRoom.Create(
                message.RoomId,
                message.RoomCode,
                message.RoomName,
                isActive: true,
                message.OccurredAtUtc);
            await db.ReplicatedRooms.AddAsync(room, ct);
        }
        else
        {
            room.UpdateDetails(message.RoomCode, message.RoomName, message.OccurredAtUtc);
        }

        if (message.EventType == RoomMaintenanceEventType.MaintenanceScheduled)
        {
            room.ScheduleMaintenance(
                message.MaintenanceBlockId,
                message.StartsAtUtc,
                message.EndsAtUtc,
                message.Reason,
                message.OccurredAtUtc);

            logger.LogInformation("Maintenance scheduled on room {RoomId}: {Start} - {End}",
                message.RoomId, message.StartsAtUtc, message.EndsAtUtc);

            // Cascading cancellation: cancel active overlapping bookings and notify students
            var conflictingBookings = await db.Bookings
                .Where(b => b.RoomId == message.RoomId &&
                            (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn) &&
                            b.TimeSlot.StartsAtUtc < message.EndsAtUtc &&
                            b.TimeSlot.EndsAtUtc > message.StartsAtUtc)
                .ToListAsync(ct);

            foreach (var booking in conflictingBookings)
            {
                booking.Cancel($"Room maintenance scheduled: {message.Reason}", message.OccurredAtUtc, "System", isAdmin: true);

                await messageBus.PublishAsync(new UserActivityIntegrationEvent(
                    UserActivityType.ReservationCancelled,
                    booking.UserId,
                    booking.Id.ToString(),
                    message.OccurredAtUtc,
                    EventId: Guid.NewGuid()));

                await messageBus.PublishAsync(new AuditIntegrationEvent(
                    ServiceSource: "Booking",
                    ResourceId: booking.Id.ToString(),
                    ActorId: "System",
                    Action: "BookingCancelledDueToMaintenance",
                    MetadataJson: $"{{\"reason\":\"{message.Reason}\",\"maintenanceBlockId\":\"{message.MaintenanceBlockId}\"}}",
                    TimestampUtc: message.OccurredAtUtc,
                    EventId: Guid.NewGuid()));

                await notificationsClient.SendAsync(new CreateNotificationRequest(
                    UserId: booking.UserId,
                    Type: "ROOM_MAINTENANCE_SCHEDULED",
                    Title: "Reserva Cancelada por Manutenção",
                    Message: $"Sua reserva '{booking.Title}' na sala {message.RoomCode} foi cancelada devido a manutenção programada: {message.Reason}",
                    Data: new { bookingId = booking.Id, roomId = message.RoomId, maintenanceBlockId = message.MaintenanceBlockId }), ct);

                logger.LogWarning("Cancelled conflicting reservation {BookingId} for User {UserId} due to maintenance in Room {RoomId}.",
                    booking.Id, booking.UserId, message.RoomId);
            }
        }
        else if (message.EventType == RoomMaintenanceEventType.MaintenanceCancelled)
        {
            room.CancelMaintenance(message.MaintenanceBlockId, message.OccurredAtUtc);

            logger.LogInformation("Maintenance block {BlockId} cancelled on room {RoomId}.",
                message.MaintenanceBlockId, message.RoomId);
        }

        RecordInboxMessage(message.EventId);
        await db.SaveChangesAsync(ct);
        TriggerThrottledLazyPruning();
    }

    public async Task HandleAsync(OperatingHoursChangedEvent message, CancellationToken ct = default)
    {
        if (await IsDuplicateEventAsync(message.EventId, ct)) return;

        var room = await db.ReplicatedRooms
            .Include(r => r.OperatingHours)
            .FirstOrDefaultAsync(r => r.Id == message.RoomId, ct);

        if (room is null)
        {
            room = ReplicatedRoom.Create(
                message.RoomId,
                $"ROOM-{message.RoomId.ToString()[..6].ToUpper()}",
                "Replicated Room",
                isActive: true,
                message.OccurredAtUtc);
            await db.ReplicatedRooms.AddAsync(room, ct);
        }

        var hours = message.OperatingHours.Select(o =>
            new ReplicatedOperatingHours(
                Guid.NewGuid(),
                message.RoomId,
                o.DayOfWeek,
                o.OpensAt,
                o.ClosesAt));

        room.SetOperatingHours(hours, message.OccurredAtUtc);
        RecordInboxMessage(message.EventId);
        await db.SaveChangesAsync(ct);
        TriggerThrottledLazyPruning();

        logger.LogInformation("Replaced operating hours for room {RoomId} ({Count} windows).",
            message.RoomId, message.OperatingHours.Count);
    }

    public async Task HandleAsync(ResourceUpdatedEvent message, CancellationToken ct = default)
    {
        if (await IsDuplicateEventAsync(message.EventId, ct)) return;

        logger.LogInformation("Received ResourceUpdatedEvent: {EventType} on Room {RoomId} for Resource {Name}",
            message.EventType, message.RoomId, message.Name);

        RecordInboxMessage(message.EventId);
        await db.SaveChangesAsync(ct);
        TriggerThrottledLazyPruning();
    }
}
