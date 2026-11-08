using Studium_Booking.Domain.Entities.ReplicatedCatalog;
using Xunit;

namespace Studium_Booking.Tests.Domain;

public class ReplicatedRoomTests
{
    private readonly Guid _roomId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ScheduleMaintenance_ShouldDetectMaintenanceOverlap()
    {
        var room = ReplicatedRoom.Create(_roomId, "LAB-101", "Lab A", true, _now);
        var blockId = Guid.NewGuid();
        var maintStart = _now.AddHours(2);
        var maintEnd = _now.AddHours(5);

        room.ScheduleMaintenance(blockId, maintStart, maintEnd, "HVAC Repairs", _now);

        Assert.True(room.IsUnderMaintenance(maintStart.AddMinutes(30), maintEnd.AddMinutes(-30)));
        Assert.True(room.IsUnderMaintenance(maintStart.AddHours(-1), maintStart.AddHours(1)));
        Assert.False(room.IsUnderMaintenance(_now, maintStart)); // Before maintenance
        Assert.False(room.IsUnderMaintenance(maintEnd, maintEnd.AddHours(1))); // After maintenance
    }

    [Fact]
    public void CancelMaintenance_ShouldClearMaintenanceOverlap()
    {
        var room = ReplicatedRoom.Create(_roomId, "LAB-101", "Lab A", true, _now);
        var blockId = Guid.NewGuid();
        var maintStart = _now.AddHours(2);
        var maintEnd = _now.AddHours(5);

        room.ScheduleMaintenance(blockId, maintStart, maintEnd, "HVAC Repairs", _now);
        Assert.True(room.IsUnderMaintenance(maintStart.AddHours(1), maintEnd.AddHours(-1)));

        room.CancelMaintenance(blockId, _now);
        Assert.False(room.IsUnderMaintenance(maintStart.AddHours(1), maintEnd.AddHours(-1)));
    }

    [Fact]
    public void SetOperatingHours_ShouldEnforceWindows()
    {
        var room = ReplicatedRoom.Create(_roomId, "ROOM-202", "Study 202", true, _now);

        // Sunday is DayOfWeek.Sunday (2026-11-08 was Sunday)
        // Set operating hours: Sunday 08:00 to 18:00
        var hours = new[]
        {
            new ReplicatedOperatingHours(Guid.NewGuid(), _roomId, DayOfWeek.Sunday, new TimeOnly(8, 0), new TimeOnly(18, 0))
        };
        room.SetOperatingHours(hours, _now);

        // Slot at 09:00 - 11:00 UTC (In UTC time zone)
        Assert.True(room.IsWithinOperatingHours(_now.Date.AddHours(9), _now.Date.AddHours(11), "UTC"));

        // Slot at 07:00 - 09:00 UTC (Opens at 08:00 -> outside)
        Assert.False(room.IsWithinOperatingHours(_now.Date.AddHours(7), _now.Date.AddHours(9), "UTC"));

        // Slot on Monday (no schedule on Monday)
        Assert.False(room.IsWithinOperatingHours(_now.Date.AddDays(1).AddHours(9), _now.Date.AddDays(1).AddHours(11), "UTC"));
    }
}
