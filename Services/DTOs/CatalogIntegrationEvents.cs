using Wolverine.Attributes;

namespace Studium_Booking.Services.DTOs;

public enum RoomMaintenanceEventType
{
    MaintenanceScheduled,
    MaintenanceCancelled
}

public enum ResourceEventType
{
    ResourceAdded,
    ResourceUpdated,
    ResourceRemoved
}

public enum ResourceType
{
    Projector,
    Whiteboard,
    Workstation,
    Microphone,
    AirConditioner,
    SoundSystem,
    VideoConference,
    LabEquipment
}

public enum ResourceStatus
{
    Operational,
    Degraded,
    OutOfOrder
}

public record OperatingHoursDto(
    DayOfWeek DayOfWeek,
    TimeOnly OpensAt,
    TimeOnly ClosesAt
);

[MessageIdentity("studium.catalog.room-maintenance")]
public record RoomMaintenanceEvent(
    RoomMaintenanceEventType EventType,
    Guid RoomId,
    Guid BuildingId,
    string RoomCode,
    string RoomName,
    Guid MaintenanceBlockId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Reason,
    DateTime OccurredAtUtc
);

[MessageIdentity("studium.catalog.operating-hours-changed")]
public record OperatingHoursChangedEvent(
    Guid RoomId,
    string TimeZone,
    IReadOnlyList<OperatingHoursDto> OperatingHours,
    DateTime OccurredAtUtc
);

[MessageIdentity("studium.catalog.room-status-changed")]
public record RoomStatusChangedEvent(
    Guid RoomId,
    bool IsActive,
    DateTime OccurredAtUtc
);

[MessageIdentity("studium.catalog.resource-updated")]
public record ResourceUpdatedEvent(
    ResourceEventType EventType,
    Guid RoomId,
    Guid ResourceId,
    string Name,
    ResourceType Type,
    int Quantity,
    ResourceStatus Status,
    DateTime OccurredAtUtc
);
