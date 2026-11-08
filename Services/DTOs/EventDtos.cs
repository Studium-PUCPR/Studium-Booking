using Wolverine.Attributes;

namespace Studium_Booking.Services.DTOs;

public enum BookingEventType
{
    ReservationCreated,
    ReservationCheckedIn,
    ReservationCancelled,
    NoShowRecorded
}

/// <summary>
/// Outbound event dispatched to studium-users-events-queue to track user booking actions and metrics.
/// </summary>
public record BookingIntegrationEvent(
    BookingEventType EventType,
    string UserId,
    string? ReservationId,
    DateTime OccurredAtUtc
);

/// <summary>
/// Outbound event dispatched to studium-notifications-queue for user alerts and institutional emails.
/// </summary>
[MessageIdentity("studium.booking.reservation-notification")]
public record ReservationNotificationEvent(
    string EventType,
    Guid BookingId,
    string UserId,
    Guid RoomId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string? AdditionalInfo,
    DateTime OccurredAtUtc
);

/// <summary>
/// Outbound event dispatched to studium-notifications-queue when a waitlist slot opens up.
/// </summary>
[MessageIdentity("studium.booking.waitlist-slot-available")]
public record WaitlistSlotAvailableEvent(
    Guid RoomId,
    Guid WaitlistEntryId,
    string UserId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime OccurredAtUtc
);
