using Studium_Booking.Domain.Entities.Waitlist;

namespace Studium_Booking.Services.DTOs;

public record JoinWaitlistRequest(
    Guid RoomId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc
);

public record WaitlistResponseDto(
    Guid Id,
    Guid RoomId,
    string UserId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    WaitlistStatus Status,
    DateTime CreatedAtUtc,
    DateTime? NotifiedAtUtc
);
