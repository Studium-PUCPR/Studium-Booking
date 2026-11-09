using Wolverine.Attributes;

namespace Studium_Booking.Services.DTOs;

/// <summary>
/// Inbound SQS message payload received from Studium-Users when a user's block status changes.
/// </summary>
[MessageIdentity("studium.users.user-status-changed")]
public record UserStatusChangedEvent(
    string UserId,
    bool IsBlocked,
    string? BlockReason,
    DateTime OccurredAtUtc,
    Guid? EventId = null
);
