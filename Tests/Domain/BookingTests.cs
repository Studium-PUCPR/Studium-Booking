using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Exceptions;
using Xunit;

namespace Studium_Booking.Tests.Domain;

public class BookingTests
{
    private readonly Guid _roomId = Guid.NewGuid();
    private const string UserId = "user-12345";
    private readonly DateTime _now = new(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ValidParameters_ShouldInstantiateConfirmedBookingWithAuditLog()
    {
        var startsAt = _now.AddHours(2);
        var endsAt = _now.AddHours(4);
        var slot = new ReservationTimeSlot(startsAt, endsAt);

        var booking = Booking.Create(_roomId, UserId, "Study Group", "Math revision", slot, _now);

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(_roomId, booking.RoomId);
        Assert.Equal(UserId, booking.UserId);
        Assert.Equal("Study Group", booking.Title);
        Assert.Equal("Math revision", booking.Purpose);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.False(string.IsNullOrWhiteSpace(booking.CheckInCode));
        Assert.Equal(6, booking.CheckInCode.Length);
        Assert.Single(booking.AuditLogs);
        Assert.Equal("Created", booking.AuditLogs.First().Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyTitle_ShouldThrowDomainException(string title)
    {
        var slot = new ReservationTimeSlot(_now.AddHours(1), _now.AddHours(2));
        var ex = Assert.Throws<DomainException>(() =>
            Booking.Create(_roomId, UserId, title, null, slot, _now));

        Assert.Equal("INVALID_TITLE", ex.ErrorCode);
    }

    [Fact]
    public void ReservationTimeSlot_StartAfterEnd_ShouldThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(1)));

        Assert.Equal("INVALID_TIME_SLOT", ex.ErrorCode);
    }

    [Fact]
    public void ReservationTimeSlot_DurationLessThan15Minutes_ShouldThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ReservationTimeSlot(_now, _now.AddMinutes(10)));

        Assert.Equal("DURATION_TOO_SHORT", ex.ErrorCode);
    }

    [Fact]
    public void ReservationTimeSlot_DurationGreaterThan12Hours_ShouldThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ReservationTimeSlot(_now, _now.AddHours(13)));

        Assert.Equal("DURATION_EXCEEDED", ex.ErrorCode);
    }

    [Fact]
    public void ReservationTimeSlot_Overlaps_ShouldReturnTrueForOverlappingSlots()
    {
        var slot1 = new ReservationTimeSlot(_now.AddHours(1), _now.AddHours(3));
        var slot2 = new ReservationTimeSlot(_now.AddHours(2), _now.AddHours(4));
        var slot3 = new ReservationTimeSlot(_now.AddHours(3), _now.AddHours(5));

        Assert.True(slot1.Overlaps(slot2));
        Assert.True(slot2.Overlaps(slot1));
        Assert.False(slot1.Overlaps(slot3)); // Adjacent boundaries do not overlap
    }

    [Fact]
    public void CheckIn_ValidCodeWithinGraceWindow_ShouldTransitionToCheckedIn()
    {
        var startsAt = _now.AddMinutes(10);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Lab session", null, slot, _now);

        // Check in at _now (10 minutes before start time -> within 15 min window)
        booking.CheckIn(booking.CheckInCode, _now, graceMinutesBefore: 15, graceMinutesAfter: 15);

        Assert.Equal(BookingStatus.CheckedIn, booking.Status);
        Assert.Equal(_now, booking.CheckedInAtUtc);
        Assert.Equal(2, booking.AuditLogs.Count);
    }

    [Fact]
    public void CheckIn_TooEarly_ShouldThrowDomainException()
    {
        var startsAt = _now.AddHours(1);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Lab session", null, slot, _now);

        // Check in 60 minutes before start time (allowed: 15 min)
        var ex = Assert.Throws<DomainException>(() =>
            booking.CheckIn(booking.CheckInCode, _now, graceMinutesBefore: 15, graceMinutesAfter: 15));

        Assert.Equal("CHECK_IN_TOO_EARLY", ex.ErrorCode);
    }

    [Fact]
    public void CheckIn_TooLate_ShouldThrowDomainException()
    {
        var startsAt = _now.AddHours(1);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Lab session", null, slot, _now);

        // Check in 20 minutes after start time (allowed: 15 min)
        var checkInTime = startsAt.AddMinutes(20);
        var ex = Assert.Throws<DomainException>(() =>
            booking.CheckIn(booking.CheckInCode, checkInTime, graceMinutesBefore: 15, graceMinutesAfter: 15));

        Assert.Equal("CHECK_IN_WINDOW_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public void CheckIn_InvalidCode_ShouldThrowDomainException()
    {
        var startsAt = _now.AddMinutes(5);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Lab session", null, slot, _now);

        var ex = Assert.Throws<DomainException>(() =>
            booking.CheckIn("WRONG1", _now));

        Assert.Equal("INVALID_CHECK_IN_CODE", ex.ErrorCode);
    }

    [Fact]
    public void CheckOut_CheckedInBooking_ShouldTransitionToCompleted()
    {
        var startsAt = _now.AddMinutes(5);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Meeting", null, slot, _now);
        booking.CheckIn(booking.CheckInCode, _now);

        var checkoutTime = _now.AddMinutes(30);
        booking.CheckOut(checkoutTime, UserId);

        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(checkoutTime, booking.CheckedOutAtUtc);
    }

    [Fact]
    public void Cancel_ConfirmedBooking_ShouldTransitionToCancelled()
    {
        var startsAt = _now.AddHours(2);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Meeting", null, slot, _now);

        booking.Cancel("Change of plans", _now.AddMinutes(10), UserId);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("Change of plans", booking.CancellationReason);
    }

    [Fact]
    public void Cancel_ByAdminForMaintenance_ShouldRecordAdminAuditLog()
    {
        var startsAt = _now.AddHours(2);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Meeting", null, slot, _now);

        booking.Cancel("Room maintenance scheduled", _now.AddMinutes(10), "System", isAdmin: true);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Contains("maintenance", booking.CancellationReason);
        Assert.Contains(booking.AuditLogs, a => a.PerformedByUserId == "System" && a.Action == "AdminCancelled");
    }

    [Fact]
    public void MarkNoShow_AfterGracePeriod_ShouldTransitionToNoShow()
    {
        var startsAt = _now.AddHours(1);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Meeting", null, slot, _now);

        var noShowTime = startsAt.AddMinutes(20); // Grace period is 15 minutes
        booking.MarkNoShow(noShowTime, graceMinutesAfter: 15);

        Assert.Equal(BookingStatus.NoShow, booking.Status);
    }

    [Fact]
    public void MarkNoShow_DuringGracePeriod_ShouldThrowDomainException()
    {
        var startsAt = _now.AddHours(1);
        var endsAt = startsAt.AddHours(1);
        var slot = new ReservationTimeSlot(startsAt, endsAt);
        var booking = Booking.Create(_roomId, UserId, "Meeting", null, slot, _now);

        var tooEarly = startsAt.AddMinutes(10); // Still within 15 minutes grace
        var ex = Assert.Throws<DomainException>(() =>
            booking.MarkNoShow(tooEarly, graceMinutesAfter: 15));

        Assert.Equal("GRACE_PERIOD_ACTIVE", ex.ErrorCode);
    }
}
