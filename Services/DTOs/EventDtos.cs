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

/// <summary>
/// Outbound message payload published to Studium-Audit for immutable compliance logging.
/// </summary>
[MessageIdentity("studium.audit.integration-event")]
public record AuditIntegrationEvent(
    string ServiceSource,
    string ResourceId,
    string ActorId,
    string Action,
    string? MetadataJson = null,
    DateTime? TimestampUtc = null
);
