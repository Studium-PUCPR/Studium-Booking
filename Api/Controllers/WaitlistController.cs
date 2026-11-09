using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Studium_Booking.Api.Extensions;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Domain.Exceptions;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Api.Controllers;

[ApiController]
[Route("api/bookings/waitlist")]
[Authorize]
public class WaitlistController(BookingDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WaitlistResponseDto>> JoinWaitlist(
        [FromBody] JoinWaitlistRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var nowUtc = DateTime.UtcNow;

        var timeSlot = new ReservationTimeSlot(request.StartsAtUtc, request.EndsAtUtc);
        var duration = timeSlot.Duration;
        var isProfessorOrAdmin = User.IsAdmin() || User.IsInRole("Professor");

        if (!isProfessorOrAdmin && duration > TimeSpan.FromHours(2))
        {
            throw new BookingConflictException("Students are restricted to a maximum reservation duration of 2 hours.");
        }

        if (duration > TimeSpan.FromHours(5))
        {
            throw new BookingConflictException("Reservation duration cannot exceed 5 hours.");
        }

        var existingEntry = await db.WaitlistEntries
            .AnyAsync(w => w.RoomId == request.RoomId &&
                           w.UserId == userId &&
                           w.Status == WaitlistStatus.Pending &&
                           w.TimeSlot.StartsAtUtc < timeSlot.EndsAtUtc &&
                           w.TimeSlot.EndsAtUtc > timeSlot.StartsAtUtc,
                      ct);

        if (existingEntry)
            throw new BookingConflictException("You are already on the waitlist for this room and time slot.");

        var entry = WaitlistEntry.Create(request.RoomId, userId, timeSlot, nowUtc);
        await db.WaitlistEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetWaitlist), new { id = entry.Id }, ToDto(entry));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WaitlistResponseDto>>> GetWaitlist(
        [FromQuery] Guid? roomId,
        [FromQuery] WaitlistStatus? status,
        CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var query = db.WaitlistEntries.AsNoTracking().AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(w => w.UserId == currentUserId);
        }

        if (roomId.HasValue)
            query = query.Where(w => w.RoomId == roomId.Value);

        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);

        var items = await query
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<WaitlistResponseDto>> CancelWaitlistEntry(Guid id, CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();
        var nowUtc = DateTime.UtcNow;

        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (entry is null)
            throw new EntityNotFoundException("WAITLIST_NOT_FOUND", $"Waitlist entry '{id}' was not found.");

        if (!isAdmin && entry.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "Only the entry owner or administrator can cancel this waitlist entry.");

        entry.Cancel(nowUtc);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(entry));
    }

    private static WaitlistResponseDto ToDto(WaitlistEntry w) =>
        new(
            w.Id,
            w.RoomId,
            w.UserId,
            w.TimeSlot.StartsAtUtc,
            w.TimeSlot.EndsAtUtc,
            w.Status,
            w.CreatedAtUtc,
            w.NotifiedAtUtc);
}
