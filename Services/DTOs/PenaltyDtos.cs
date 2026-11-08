using Studium_Booking.Domain.Entities.Penalties;

namespace Studium_Booking.Services.DTOs;

public record PenaltyResponseDto(
    Guid Id,
    string UserId,
    Guid? BookingId,
    PenaltyType Type,
    string Reason,
    DateTime IssuedAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsActive,
    string? RevokedReason,
    DateTime? RevokedAtUtc
);

public record RevokePenaltyRequest(
    string Reason
);
