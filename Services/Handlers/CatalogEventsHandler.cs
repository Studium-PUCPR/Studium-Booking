using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.ReplicatedCatalog;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Services.Handlers;

public class CatalogEventsHandler(
    BookingDbContext db,
    ILogger<CatalogEventsHandler> logger)
{
    public async Task HandleAsync(RoomStatusChangedEvent message, CancellationToken ct = default)
    {
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

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Replicated room {RoomId} status updated to IsActive={IsActive}.",
            message.RoomId, message.IsActive);
    }

    public async Task HandleAsync(RoomMaintenanceEvent message, CancellationToken ct = default)
    {
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
        }
        else if (message.EventType == RoomMaintenanceEventType.MaintenanceCancelled)
        {
            room.CancelMaintenance(message.MaintenanceBlockId, message.OccurredAtUtc);

            logger.LogInformation("Maintenance block {BlockId} cancelled on room {RoomId}.",
                message.MaintenanceBlockId, message.RoomId);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(OperatingHoursChangedEvent message, CancellationToken ct = default)
    {
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
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Replaced operating hours for room {RoomId} ({Count} windows).",
            message.RoomId, message.OperatingHours.Count);
    }

    public Task HandleAsync(ResourceUpdatedEvent message, CancellationToken ct = default)
    {
        logger.LogInformation("Received ResourceUpdatedEvent: {EventType} on Room {RoomId} for Resource {Name}",
            message.EventType, message.RoomId, message.Name);
        return Task.CompletedTask;
    }
}
