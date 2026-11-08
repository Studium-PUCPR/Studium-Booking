namespace Studium_Booking.Services.DTOs;

public enum RoomAvailabilityStatus
{
    Available = 0,
    Reserved = 1,
    Occupied = 2,
    Maintenance = 3,
    Inactive = 4
}

public record RoomStatusResponseDto(
    Guid RoomId,
    string? RoomCode,
    string? RoomName,
    RoomAvailabilityStatus Status,
    string? StatusReason,
    BookingResponseDto? CurrentBooking,
    BookingResponseDto? NextBooking
);

public record RoomTimelineItemDto(
    Guid Id,
    string ItemType,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Title,
    string? Details
);

public record RoomTimelineResponseDto(
    Guid RoomId,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<RoomTimelineItemDto> Items
);
