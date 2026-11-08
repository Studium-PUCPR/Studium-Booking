using System.Security.Cryptography;
using Studium_Booking.Domain.Common;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Domain.Entities.Bookings;

public class Booking
{
    private readonly List<BookingAuditLog> _auditLogs = [];

    public Guid Id { get; init; }
    public Guid RoomId { get; init; }
    public string UserId { get; init; } = default!;
    public string Title { get; private set; } = default!;
    public string? Purpose { get; private set; }
    public ReservationTimeSlot TimeSlot { get; private set; } = default!;
    public BookingStatus Status { get; private set; }
    public string CheckInCode { get; private set; } = default!;
    public DateTime? CheckedInAtUtc { get; private set; }
    public DateTime? CheckedOutAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<BookingAuditLog> AuditLogs => _auditLogs.AsReadOnly();

    private Booking() { }

    public static Booking Create(
        Guid roomId,
        string userId,
        string title,
        string? purpose,
        ReservationTimeSlot timeSlot,
        DateTime nowUtc,
        string? customCheckInCode = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("INVALID_USER_ID", "User identifier cannot be empty.");

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("INVALID_TITLE", "Reservation title cannot be empty.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        var bookingId = Guid.NewGuid();
        var checkInCode = string.IsNullOrWhiteSpace(customCheckInCode)
            ? GenerateCheckInCode()
            : customCheckInCode.Trim().ToUpperInvariant();

        var booking = new Booking
        {
            Id = bookingId,
            RoomId = roomId,
            UserId = userId.Trim(),
            Title = title.Trim(),
            Purpose = purpose?.Trim(),
            TimeSlot = timeSlot,
            Status = BookingStatus.Confirmed,
            CheckInCode = checkInCode,
            CreatedAtUtc = normalizedNow,
            UpdatedAtUtc = normalizedNow
        };

        booking._auditLogs.Add(new BookingAuditLog(
            Guid.NewGuid(),
            bookingId,
            "Created",
            userId,
            $"Reservation created for time slot {timeSlot.StartsAtUtc:O} - {timeSlot.EndsAtUtc:O}",
            normalizedNow
        ));

        return booking;
    }

    public void CheckIn(
        string code,
        DateTime nowUtc,
        int graceMinutesBefore = 15,
        int graceMinutesAfter = 15)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException("INVALID_STATUS", $"Cannot check in a reservation with status '{Status}'. Only Confirmed bookings can be checked in.");

        if (string.IsNullOrWhiteSpace(code) || !string.Equals(code.Trim(), CheckInCode, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("INVALID_CHECK_IN_CODE", "Invalid check-in code provided.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        var earliestAllowed = TimeSlot.StartsAtUtc.AddMinutes(-graceMinutesBefore);
        var latestAllowed = TimeSlot.StartsAtUtc.AddMinutes(graceMinutesAfter);

        if (normalizedNow < earliestAllowed)
            throw new DomainException("CHECK_IN_TOO_EARLY", $"Check-in opens {graceMinutesBefore} minutes prior to start time ({earliestAllowed:O}).");

        if (normalizedNow > latestAllowed)
            throw new DomainException("CHECK_IN_WINDOW_EXPIRED", $"Check-in window closed {graceMinutesAfter} minutes after start time ({latestAllowed:O}).");

        Status = BookingStatus.CheckedIn;
        CheckedInAtUtc = normalizedNow;
        UpdatedAtUtc = normalizedNow;

        _auditLogs.Add(new BookingAuditLog(
            Guid.NewGuid(),
            Id,
            "CheckedIn",
            UserId,
            "User successfully checked in using QR/Check-in code.",
            normalizedNow
        ));
    }

    public void CheckOut(DateTime nowUtc, string performedByUserId)
    {
        if (Status != BookingStatus.CheckedIn)
            throw new DomainException("INVALID_STATUS", $"Cannot check out a reservation with status '{Status}'. Only CheckedIn bookings can be completed.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        Status = BookingStatus.Completed;
        CheckedOutAtUtc = normalizedNow;
        UpdatedAtUtc = normalizedNow;

        _auditLogs.Add(new BookingAuditLog(
            Guid.NewGuid(),
            Id,
            "Completed",
            performedByUserId,
            "Reservation marked completed after check-out.",
            normalizedNow
        ));
    }

    public void Cancel(string reason, DateTime nowUtc, string cancelledByUserId, bool isAdmin = false)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException("INVALID_STATUS", $"Cannot cancel a reservation with status '{Status}'. Only Confirmed bookings can be cancelled.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("CANCELLATION_REASON_REQUIRED", "A reason must be provided when cancelling a reservation.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        Status = BookingStatus.Cancelled;
        CancelledAtUtc = normalizedNow;
        CancellationReason = reason.Trim();
        UpdatedAtUtc = normalizedNow;

        _auditLogs.Add(new BookingAuditLog(
            Guid.NewGuid(),
            Id,
            isAdmin ? "AdminCancelled" : "UserCancelled",
            cancelledByUserId,
            $"Reservation cancelled: {CancellationReason}",
            normalizedNow
        ));
    }

    public void MarkNoShow(DateTime nowUtc, int graceMinutesAfter = 15)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException("INVALID_STATUS", $"Cannot mark a reservation as No-Show with status '{Status}'.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        var latestAllowedCheckIn = TimeSlot.StartsAtUtc.AddMinutes(graceMinutesAfter);

        if (normalizedNow <= latestAllowedCheckIn)
            throw new DomainException("GRACE_PERIOD_ACTIVE", $"Cannot mark as No-Show before check-in grace period has passed ({latestAllowedCheckIn:O}).");

        Status = BookingStatus.NoShow;
        UpdatedAtUtc = normalizedNow;

        _auditLogs.Add(new BookingAuditLog(
            Guid.NewGuid(),
            Id,
            "NoShowRecorded",
            "System",
            $"User did not check in within {graceMinutesAfter} minutes of reservation start time.",
            normalizedNow
        ));
    }

    public static string GenerateCheckInCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var buffer = new byte[6];
        RandomNumberGenerator.Fill(buffer);
        var result = new char[6];
        for (var i = 0; i < 6; i++)
        {
            result[i] = chars[buffer[i] % chars.Length];
        }
        return new string(result);
    }
}
