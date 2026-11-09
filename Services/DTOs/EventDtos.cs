using Wolverine.Attributes;

namespace Studium_Booking.Services.DTOs;

public enum UserActivityType
{
    ReservationCreated,
    ReservationCheckedIn,
    ReservationCancelled,
    NoShowRecorded
}

/// <summary>
/// Outbound event dispatched to studium-users-events-queue to track user activity metrics and standing.
/// </summary>
[MessageIdentity("studium.users.user-activity")]
public record UserActivityIntegrationEvent(
    UserActivityType EventType,
    string UserId,
    string? ActivityReferenceId,
    DateTime OccurredAtUtc
);
