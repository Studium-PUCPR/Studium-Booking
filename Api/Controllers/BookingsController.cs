using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Studium_Booking.Api.Extensions;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Domain.Exceptions;
using Studium_Booking.Services.Clients;
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
    INotificationsClient notificationsClient,
    ILogger<BookingsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking(
        [FromBody] CreateBookingRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var nowUtc = DateTime.UtcNow;

        var isBlocked = await db.BlockedUsers.AnyAsync(
            u => u.UserId == userId && u.IsBlocked,
            ct);

        if (isBlocked)
            throw new UserBlockedException("User account is currently blocked from making reservations.");

        var timeSlot = new ReservationTimeSlot(request.StartsAtUtc, request.EndsAtUtc);

        // Small tolerance so a reservation for "now" picked in HH:mm granularity is still accepted.
        if (timeSlot.StartsAtUtc < nowUtc.AddMinutes(-5))
            throw new DomainException("START_IN_PAST", "Reservation start time cannot be in the past.");

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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ExclusionViolation })
        {
            throw new BookingConflictException("The selected room was just reserved by another student for an overlapping time slot.");
        }

        // Schedule automated 15-minute No-Show evaluation message
        var graceDeadline = booking.TimeSlot.StartsAtUtc.AddMinutes(options.Value.GraceMinutesAfterCheckIn);
        var noShowDelay = graceDeadline - nowUtc;
        if (noShowDelay > TimeSpan.Zero)
        {
            await messageBus.ScheduleAsync(new CheckReservationNoShowMessage(booking.Id), noShowDelay);
        }

        await messageBus.PublishAsync(new UserActivityIntegrationEvent(
            UserActivityType.ReservationCreated,
            userId,
            booking.Id.ToString(),
            nowUtc,
            EventId: Guid.NewGuid()));

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: userId,
            Action: "BookingCreated",
            MetadataJson: $"{{\"title\":\"{booking.Title}\",\"roomId\":\"{booking.RoomId}\"}}",
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid()));

        await notificationsClient.SendAsync(new CreateNotificationRequest(
            UserId: userId,
            Type: "RESERVATION_CREATED",
            Title: "Reserva Confirmada",
            Message: $"Reserva '{booking.Title}' criada com sucesso.",
            Data: new { bookingId = booking.Id, roomId = booking.RoomId }), ct);

        logger.LogInformation("Reservation {BookingId} created for User {UserId} in Room {RoomId}.",
            booking.Id, userId, booking.RoomId);

        // Promote waitlist entry if creator was waitlisted
        var activeWaitlist = await db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.RoomId == booking.RoomId &&
                                      w.UserId == userId &&
                                      (w.Status == WaitlistStatus.Notified || w.Status == WaitlistStatus.Pending) &&
                                      w.TimeSlot.StartsAtUtc < booking.TimeSlot.EndsAtUtc &&
                                      w.TimeSlot.EndsAtUtc > booking.TimeSlot.StartsAtUtc, ct);
        if (activeWaitlist is not null)
        {
            activeWaitlist.Promote(nowUtc);
            await db.SaveChangesAsync(ct);
        }

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

        await messageBus.PublishAsync(new UserActivityIntegrationEvent(
            UserActivityType.ReservationCheckedIn,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc,
            EventId: Guid.NewGuid()));

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: booking.UserId,
            Action: "BookingCheckedIn",
            MetadataJson: $"{{\"roomId\":\"{booking.RoomId}\"}}",
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid()));

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

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: currentUserId,
            Action: "BookingCheckedOut",
            MetadataJson: $"{{\"roomId\":\"{booking.RoomId}\"}}",
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid()));

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

        await messageBus.PublishAsync(new UserActivityIntegrationEvent(
            UserActivityType.ReservationCancelled,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc,
            EventId: Guid.NewGuid()));

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: currentUserId,
            Action: "BookingCancelled",
            MetadataJson: $"{{\"reason\":\"{request.Reason}\"}}",
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid()));

        await notificationsClient.SendAsync(new CreateNotificationRequest(
            UserId: booking.UserId,
            Type: "RESERVATION_CANCELLED",
            Title: "Reserva Cancelada",
            Message: $"Reserva '{booking.Title}' foi cancelada: {request.Reason}",
            Data: new { bookingId = booking.Id, roomId = booking.RoomId }), ct);

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

            await messageBus.ScheduleAsync(
                new CheckWaitlistClaimDeadlineMessage(nextWaitlist.Id),
                TimeSpan.FromMinutes(options.Value.WaitlistClaimGraceMinutes));

            await notificationsClient.SendAsync(new CreateNotificationRequest(
                UserId: nextWaitlist.UserId,
                Type: "WAITLIST_SLOT_AVAILABLE",
                Title: "Vaga Disponível na Fila de Espera",
                Message: $"Uma vaga foi liberada na sala {booking.RoomId}. Acesse a plataforma dentro de {options.Value.WaitlistClaimGraceMinutes} minutos para confirmar sua reserva.",
                Data: new { roomId = booking.RoomId, waitlistId = nextWaitlist.Id }), ct);

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
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();
        if (!isAdmin)
            throw new DomainException("FORBIDDEN", "Only administrators can manually mark reservations as No-Show.");

        var nowUtc = DateTime.UtcNow;
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
            throw new EntityNotFoundException("BOOKING_NOT_FOUND", $"Reservation '{id}' was not found.");

        booking.MarkNoShow(nowUtc, options.Value.GraceMinutesAfterCheckIn);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new UserActivityIntegrationEvent(
            UserActivityType.NoShowRecorded,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc,
            EventId: Guid.NewGuid()));

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: currentUserId,
            Action: "NoShowRecorded",
            MetadataJson: $"{{\"roomId\":\"{booking.RoomId}\"}}",
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid()));

        await notificationsClient.SendAsync(new CreateNotificationRequest(
            UserId: booking.UserId,
            Type: "NO_SHOW_RECORDED",
            Title: "Prazo de Presença Expirado",
            Message: "Reserva marcada como ausência (No-Show).",
            Data: new { bookingId = booking.Id, roomId = booking.RoomId }), ct);

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
