using Studium_Booking.Domain.Entities.Bookings;

namespace Studium_Booking.Services.DTOs;

public record CreateBookingRequest(
    Guid RoomId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Title,
    string? Purpose
);

public record BookingResponseDto(
    Guid Id,
    Guid RoomId,
    string UserId,
    string Title,
    string? Purpose,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    BookingStatus Status,
    string CheckInCode,
    DateTime? CheckedInAtUtc,
    DateTime? CheckedOutAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    DateTime CreatedAtUtc
);

public record BookingAuditLogDto(
    Guid Id,
    string Action,
    string PerformedByUserId,
    string? Details,
    DateTime TimestampUtc
);

public record BookingDetailsDto(
    Guid Id,
    Guid RoomId,
    string UserId,
    string Title,
    string? Purpose,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    BookingStatus Status,
    string CheckInCode,
    DateTime? CheckedInAtUtc,
    DateTime? CheckedOutAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    IReadOnlyList<BookingAuditLogDto> AuditLogs
);

public record CheckInRequest(
    string CheckInCode
);

public record CancelBookingRequest(
    string Reason
);

public record QrCodeResponseDto(
    Guid BookingId,
    string CheckInCode,
    string QrContent,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int GraceMinutesBefore,
    int GraceMinutesAfter
);
