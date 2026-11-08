using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Api.Controllers;

[ApiController]
[Route("api/bookings/rooms")]
[Authorize]
public class RoomsController(BookingDbContext db) : ControllerBase
{
    [HttpGet("{roomId:guid}/status")]
    public async Task<ActionResult<RoomStatusResponseDto>> GetRoomStatus(
        Guid roomId,
        [FromQuery] DateTime? atUtc,
        CancellationToken ct)
    {
        var targetUtc = atUtc.HasValue ? atUtc.Value.ToUniversalTime() : DateTime.UtcNow;

        var room = await db.ReplicatedRooms
            .Include(r => r.MaintenanceBlocks)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        string? roomCode = room?.Code;
        string? roomName = room?.Name;

        if (room is not null && !room.IsActive)
        {
            return Ok(new RoomStatusResponseDto(
                roomId,
                roomCode,
                roomName,
                RoomAvailabilityStatus.Inactive,
                "Room is marked inactive in catalog.",
                null,
                null));
        }

        var maintenance = room?.MaintenanceBlocks
            .FirstOrDefault(m => m.StartsAtUtc <= targetUtc && m.EndsAtUtc >= targetUtc);

        if (maintenance is not null)
        {
            return Ok(new RoomStatusResponseDto(
                roomId,
                roomCode,
                roomName,
                RoomAvailabilityStatus.Maintenance,
                $"Under maintenance: {maintenance.Reason}",
                null,
                null));
        }

        var activeBookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.RoomId == roomId &&
                        (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn) &&
                        b.TimeSlot.EndsAtUtc >= targetUtc)
            .OrderBy(b => b.TimeSlot.StartsAtUtc)
            .Take(2)
            .ToListAsync(ct);

        var currentBooking = activeBookings.FirstOrDefault(
            b => b.TimeSlot.StartsAtUtc <= targetUtc && b.TimeSlot.EndsAtUtc >= targetUtc);

        var nextBooking = activeBookings.FirstOrDefault(
            b => b.TimeSlot.StartsAtUtc > targetUtc);

        var currentBookingDto = currentBooking is not null ? ToDto(currentBooking) : null;
        var nextBookingDto = nextBooking is not null ? ToDto(nextBooking) : null;

        var status = currentBooking switch
        {
            { Status: BookingStatus.CheckedIn } => RoomAvailabilityStatus.Occupied,
            { Status: BookingStatus.Confirmed } => RoomAvailabilityStatus.Reserved,
            _ => RoomAvailabilityStatus.Available
        };

        var reason = status switch
        {
            RoomAvailabilityStatus.Occupied => $"Occupied by reservation '{currentBooking?.Title}'.",
            RoomAvailabilityStatus.Reserved => $"Reserved for '{currentBooking?.Title}' (check-in pending).",
            _ => "Room is available."
        };

        return Ok(new RoomStatusResponseDto(
            roomId,
            roomCode,
            roomName,
            status,
            reason,
            currentBookingDto,
            nextBookingDto));
    }

    [HttpGet("{roomId:guid}/timeline")]
    public async Task<ActionResult<RoomTimelineResponseDto>> GetRoomTimeline(
        Guid roomId,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken ct)
    {
        var normalizedFrom = fromUtc.ToUniversalTime();
        var normalizedTo = toUtc.ToUniversalTime();

        var room = await db.ReplicatedRooms
            .Include(r => r.MaintenanceBlocks)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        var items = new List<RoomTimelineItemDto>();

        if (room is not null)
        {
            var blocks = room.MaintenanceBlocks
                .Where(m => m.StartsAtUtc < normalizedTo && m.EndsAtUtc > normalizedFrom);

            foreach (var b in blocks)
            {
                items.Add(new RoomTimelineItemDto(
                    b.Id,
                    "Maintenance",
                    b.StartsAtUtc,
                    b.EndsAtUtc,
                    "Maintenance Window",
                    b.Reason));
            }
        }

        var bookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.RoomId == roomId &&
                        b.Status != BookingStatus.Cancelled &&
                        b.TimeSlot.StartsAtUtc < normalizedTo &&
                        b.TimeSlot.EndsAtUtc > normalizedFrom)
            .ToListAsync(ct);

        foreach (var b in bookings)
        {
            items.Add(new RoomTimelineItemDto(
                b.Id,
                b.Status.ToString(),
                b.TimeSlot.StartsAtUtc,
                b.TimeSlot.EndsAtUtc,
                b.Title,
                b.Purpose));
        }

        items = items.OrderBy(i => i.StartsAtUtc).ToList();

        return Ok(new RoomTimelineResponseDto(roomId, normalizedFrom, normalizedTo, items));
    }

    private static BookingResponseDto ToDto(Booking b) =>
        new(
            b.Id,
            b.RoomId,
            b.UserId,
            b.Title,
            b.Purpose,
            b.TimeSlot.StartsAtUtc,
            b.TimeSlot.EndsAtUtc,
            b.Status,
            b.CheckInCode,
            b.CheckedInAtUtc,
            b.CheckedOutAtUtc,
            b.CancelledAtUtc,
            b.CancellationReason,
            b.CreatedAtUtc);
}
