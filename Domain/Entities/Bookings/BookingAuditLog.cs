namespace Studium_Booking.Domain.Entities.Bookings;

public record BookingAuditLog(
    Guid Id,
    Guid BookingId,
    string Action,
    string PerformedByUserId,
    string? Details,
    DateTime TimestampUtc
);
