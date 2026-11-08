using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Domain.Exceptions;
using Xunit;

namespace Studium_Booking.Tests.Domain;

public class WaitlistTests
{
    private readonly Guid _roomId = Guid.NewGuid();
    private const string UserId = "user-waitlist";
    private readonly DateTime _now = new(2026, 11, 8, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ShouldInstantiatePendingEntry()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(4));
        var entry = WaitlistEntry.Create(_roomId, UserId, slot, _now);

        Assert.Equal(WaitlistStatus.Pending, entry.Status);
        Assert.Equal(_roomId, entry.RoomId);
        Assert.Equal(UserId, entry.UserId);
        Assert.Null(entry.NotifiedAtUtc);
    }

    [Fact]
    public void Notify_PendingEntry_ShouldTransitionToNotified()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(4));
        var entry = WaitlistEntry.Create(_roomId, UserId, slot, _now);

        var notifyTime = _now.AddMinutes(30);
        entry.Notify(notifyTime);

        Assert.Equal(WaitlistStatus.Notified, entry.Status);
        Assert.Equal(notifyTime, entry.NotifiedAtUtc);
    }

    [Fact]
    public void Promote_NotifiedEntry_ShouldTransitionToPromoted()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(4));
        var entry = WaitlistEntry.Create(_roomId, UserId, slot, _now);
        entry.Notify(_now.AddMinutes(10));

        entry.Promote(_now.AddMinutes(15));
        Assert.Equal(WaitlistStatus.Promoted, entry.Status);
    }

    [Fact]
    public void Cancel_PendingEntry_ShouldTransitionToCancelled()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(4));
        var entry = WaitlistEntry.Create(_roomId, UserId, slot, _now);

        entry.Cancel(_now.AddMinutes(5));
        Assert.Equal(WaitlistStatus.Cancelled, entry.Status);
    }
}
