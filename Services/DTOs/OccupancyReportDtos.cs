namespace Studium_Booking.Services.DTOs;

public record HourlyOccupancyDto(
    int HourOfDay,
    int BookingsCount
);

public record RoomOccupancyDto(
    Guid RoomId,
    int TotalBookings,
    double TotalHours
);

public record OccupancyReportDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalBookings,
    int ConfirmedCount,
    int CheckedInCount,
    int CompletedCount,
    int CancelledCount,
    int NoShowCount,
    double CheckInRatePercent,
    double CancellationRatePercent,
    double NoShowRatePercent,
    double TotalBookedHours,
    IReadOnlyList<HourlyOccupancyDto> PeakHours,
    IReadOnlyList<RoomOccupancyDto> RoomBreakdown
);
