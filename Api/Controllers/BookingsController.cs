using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Studium_Booking.Api.Extensions;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Penalties;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Domain.Exceptions;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;
using Wolverine;

namespace Studium_Booking.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(
    BookingDbContext db,
    IMessageBus messageBus,
    IOptions<BookingOptions> options,
    ILogger<BookingsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking(
        [FromBody] CreateBookingRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var nowUtc = DateTime.UtcNow;

        var isBlocked = await db.Penalties.AnyAsync(
            p => p.UserId == userId &&
                 p.IsActive &&
                 p.Type == PenaltyType.Suspension &&
                 (p.ExpiresAtUtc == null || p.ExpiresAtUtc > nowUtc),
            ct);

        if (isBlocked)
            throw new UserBlockedException("User is currently suspended from making reservations due to active penalties.");

        var timeSlot = new ReservationTimeSlot(request.StartsAtUtc, request.EndsAtUtc);

        var room = await db.ReplicatedRooms
            .Include(r => r.MaintenanceBlocks)
            .Include(r => r.OperatingHours)
            .FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);

        if (room is not null)
        {
            if (!room.IsActive)
                throw new BookingConflictException("The selected room is inactive and currently unavailable for reservations.");

            if (room.IsUnderMaintenance(timeSlot.StartsAtUtc, timeSlot.EndsAtUtc))
                throw new BookingConflictException("The selected room is undergoing scheduled maintenance during the requested time slot.");

            if (!room.IsWithinOperatingHours(timeSlot.StartsAtUtc, timeSlot.EndsAtUtc, options.Value.DefaultTimeZone))
                throw new BookingConflictException("The requested reservation interval falls outside the room's operating hours.");
        }

        var roomConflict = await db.Bookings.AnyAsync(
            b => b.RoomId == request.RoomId &&
                 (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn) &&
                 b.TimeSlot.StartsAtUtc < timeSlot.EndsAtUtc &&
                 b.TimeSlot.EndsAtUtc > timeSlot.StartsAtUtc,
            ct);

        if (roomConflict)
            throw new BookingConflictException("The room is already reserved for the selected time slot.");

        var userConflict = await db.Bookings.AnyAsync(
            b => b.UserId == userId &&
                 (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn) &&
                 b.TimeSlot.StartsAtUtc < timeSlot.EndsAtUtc &&
                 b.TimeSlot.EndsAtUtc > timeSlot.StartsAtUtc,
            ct);

        if (userConflict)
            throw new BookingConflictException("You already have an active overlapping reservation during this time window.");

        var booking = Booking.Create(
            request.RoomId,
            userId,
            request.Title,
            request.Purpose,
            timeSlot,
            nowUtc);

        await db.Bookings.AddAsync(booking, ct);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BookingIntegrationEvent(
            BookingEventType.ReservationCreated,
            userId,
            booking.Id.ToString(),
            nowUtc));

        await messageBus.PublishAsync(new ReservationNotificationEvent(
            "ReservationCreated",
            booking.Id,
            userId,
            booking.RoomId,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            $"Reservation '{booking.Title}' created successfully.",
            nowUtc));

        logger.LogInformation("Reservation {BookingId} created for User {UserId} in Room {RoomId}.",
            booking.Id, userId, booking.RoomId);

        var response = ToDto(booking);
        return CreatedAtAction(nameof(GetBookingById), new { id = booking.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingResponseDto>>> GetBookings(
        [FromQuery] Guid? roomId,
        [FromQuery] string? userId,
        [FromQuery] BookingStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var query = db.Bookings.AsNoTracking().AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(b => b.UserId == currentUserId);
        }
        else if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(b => b.UserId == userId.Trim());
        }

        if (roomId.HasValue)
            query = query.Where(b => b.RoomId == roomId.Value);

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        if (fromUtc.HasValue)
            query = query.Where(b => b.TimeSlot.EndsAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(b => b.TimeSlot.StartsAtUtc <= toUtc.Value);

        var pageClamped = Math.Max(1, page);
        var sizeClamped = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(b => b.TimeSlot.StartsAtUtc)
            .Skip((pageClamped - 1) * sizeClamped)
            .Take(sizeClamped)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDetailsDto>> GetBookingById(Guid id, CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var booking = await db.Bookings
            .Include(b => b.AuditLogs)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        if (!isAdmin && booking.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "You do not have access to view this reservation.");

        var auditLogs = booking.AuditLogs
            .OrderBy(a => a.TimestampUtc)
            .Select(a => new BookingAuditLogDto(a.Id, a.Action, a.PerformedByUserId, a.Details, a.TimestampUtc))
            .ToList();

        var response = new BookingDetailsDto(
            booking.Id,
            booking.RoomId,
            booking.UserId,
            booking.Title,
            booking.Purpose,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            booking.Status,
            booking.CheckInCode,
            booking.CheckedInAtUtc,
            booking.CheckedOutAtUtc,
            booking.CancelledAtUtc,
            booking.CancellationReason,
            booking.CreatedAtUtc,
            auditLogs);

        return Ok(response);
    }

    [HttpPost("{id:guid}/check-in")]
    public async Task<ActionResult<BookingResponseDto>> CheckIn(
        Guid id,
        [FromBody] CheckInRequest request,
        CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();
        var nowUtc = DateTime.UtcNow;

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        if (!isAdmin && booking.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "Only the reservation owner or administrator can perform check-in.");

        booking.CheckIn(
            request.CheckInCode,
            nowUtc,
            options.Value.GraceMinutesBeforeCheckIn,
            options.Value.GraceMinutesAfterCheckIn);

        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BookingIntegrationEvent(
            BookingEventType.ReservationCheckedIn,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc));

        logger.LogInformation("Reservation {BookingId} checked in successfully at {Time}.", id, nowUtc);
        return Ok(ToDto(booking));
    }

    [HttpPost("{id:guid}/check-out")]
    public async Task<ActionResult<BookingResponseDto>> CheckOut(Guid id, CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();
        var nowUtc = DateTime.UtcNow;

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        if (!isAdmin && booking.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "Only the reservation owner or administrator can perform check-out.");

        booking.CheckOut(nowUtc, currentUserId);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Reservation {BookingId} checked out successfully at {Time}.", id, nowUtc);
        return Ok(ToDto(booking));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BookingResponseDto>> Cancel(
        Guid id,
        [FromBody] CancelBookingRequest request,
        CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();
        var nowUtc = DateTime.UtcNow;

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        if (!isAdmin && booking.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "Only the reservation owner or administrator can cancel this reservation.");

        booking.Cancel(request.Reason, nowUtc, currentUserId, isAdmin);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BookingIntegrationEvent(
            BookingEventType.ReservationCancelled,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc));

        await messageBus.PublishAsync(new ReservationNotificationEvent(
            "ReservationCancelled",
            booking.Id,
            booking.UserId,
            booking.RoomId,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            $"Reservation cancelled: {request.Reason}",
            nowUtc));

        // Waitlist slot promotion logic (UC016)
        var nextWaitlist = await db.WaitlistEntries
            .Where(w => w.RoomId == booking.RoomId &&
                        w.Status == WaitlistStatus.Pending &&
                        w.TimeSlot.StartsAtUtc < booking.TimeSlot.EndsAtUtc &&
                        w.TimeSlot.EndsAtUtc > booking.TimeSlot.StartsAtUtc)
            .OrderBy(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (nextWaitlist is not null)
        {
            nextWaitlist.Notify(nowUtc);
            await db.SaveChangesAsync(ct);

            await messageBus.PublishAsync(new WaitlistSlotAvailableEvent(
                booking.RoomId,
                nextWaitlist.Id,
                nextWaitlist.UserId,
                booking.TimeSlot.StartsAtUtc,
                booking.TimeSlot.EndsAtUtc,
                nowUtc));

            logger.LogInformation("Notified waitlist candidate {WaitlistId} for newly freed slot in Room {RoomId}.",
                nextWaitlist.Id, booking.RoomId);
        }

        return Ok(ToDto(booking));
    }

    [HttpGet("{id:guid}/qr-code")]
    public async Task<ActionResult<QrCodeResponseDto>> GetQrCode(Guid id, CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        if (!isAdmin && booking.UserId != currentUserId)
            throw new DomainException("FORBIDDEN", "You do not have access to this reservation's QR check-in code.");

        var qrContent = $"studium:booking:{booking.Id}:{booking.CheckInCode}";

        return Ok(new QrCodeResponseDto(
            booking.Id,
            booking.CheckInCode,
            qrContent,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            options.Value.GraceMinutesBeforeCheckIn,
            options.Value.GraceMinutesAfterCheckIn));
    }

    [HttpPost("{id:guid}/mark-no-show")]
    public async Task<ActionResult<BookingResponseDto>> MarkNoShow(Guid id, CancellationToken ct)
    {
        var isAdmin = User.IsAdmin();
        if (!isAdmin)
            throw new DomainException("FORBIDDEN", "Only administrators can manually mark reservations as No-Show.");

        var nowUtc = DateTime.UtcNow;
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        booking.MarkNoShow(nowUtc, options.Value.GraceMinutesAfterCheckIn);

        // Record penalty
        var activeInfractionsCount = await db.Penalties.CountAsync(
            p => p.UserId == booking.UserId &&
                 p.Type == PenaltyType.NoShowInfraction &&
                 p.IsActive,
            ct);

        var penaltyType = activeInfractionsCount >= (options.Value.MaxNoShowsBeforeSuspension - 1)
            ? PenaltyType.Suspension
            : PenaltyType.NoShowInfraction;

        var penalty = PenaltyRecord.Create(
            booking.UserId,
            penaltyType,
            $"Automated penalty issued for no-show on reservation {booking.Id}.",
            nowUtc,
            booking.Id,
            penaltyType == PenaltyType.Suspension ? TimeSpan.FromDays(options.Value.DefaultSuspensionDays) : null);

        await db.Penalties.AddAsync(penalty, ct);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BookingIntegrationEvent(
            BookingEventType.NoShowRecorded,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc));

        await messageBus.PublishAsync(new ReservationNotificationEvent(
            "NoShowRecorded",
            booking.Id,
            booking.UserId,
            booking.RoomId,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            $"Reservation marked as No-Show. A {penaltyType} penalty was recorded.",
            nowUtc));

        return Ok(ToDto(booking));
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
