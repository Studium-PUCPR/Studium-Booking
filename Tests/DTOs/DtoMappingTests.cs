using System.Text.Json;
using System.Text.Json.Serialization;
using Mapster;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Penalties;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Services.DTOs;
using Xunit;

namespace Studium_Booking.Tests.DTOs;

public class DtoMappingTests
{
    private readonly Guid _roomId = Guid.NewGuid();
    private const string UserId = "user-mapster";
    private readonly DateTime _now = new(2026, 11, 8, 15, 30, 0, DateTimeKind.Utc);

    public DtoMappingTests()
    {
        BookingMappingConfig.Configure();
    }

    [Fact]
    public void Mapster_AdaptsBookingEntity_ToBookingResponseDto()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(1), _now.AddHours(2));
        var booking = Booking.Create(_roomId, UserId, "Linear Algebra", "Exam prep", slot, _now);

        var dto = booking.Adapt<BookingResponseDto>();

        Assert.Equal(booking.Id, dto.Id);
        Assert.Equal(booking.RoomId, dto.RoomId);
        Assert.Equal(booking.UserId, dto.UserId);
        Assert.Equal(booking.Title, dto.Title);
        Assert.Equal(booking.Purpose, dto.Purpose);
        Assert.Equal(booking.TimeSlot.StartsAtUtc, dto.StartsAtUtc);
        Assert.Equal(booking.TimeSlot.EndsAtUtc, dto.EndsAtUtc);
        Assert.Equal(booking.Status, dto.Status);
        Assert.Equal(booking.CheckInCode, dto.CheckInCode);
    }

    [Fact]
    public void Mapster_AdaptsPenaltyRecord_ToPenaltyResponseDto()
    {
        var penalty = PenaltyRecord.Create(UserId, PenaltyType.Suspension, "Rule violation", _now);
        var dto = penalty.Adapt<PenaltyResponseDto>();

        Assert.Equal(penalty.Id, dto.Id);
        Assert.Equal(penalty.UserId, dto.UserId);
        Assert.Equal(penalty.Type, dto.Type);
        Assert.Equal(penalty.Reason, dto.Reason);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void Mapster_AdaptsWaitlistEntry_ToWaitlistResponseDto()
    {
        var slot = new ReservationTimeSlot(_now.AddHours(1), _now.AddHours(3));
        var entry = WaitlistEntry.Create(_roomId, UserId, slot, _now);
        var dto = entry.Adapt<WaitlistResponseDto>();

        Assert.Equal(entry.Id, dto.Id);
        Assert.Equal(entry.RoomId, dto.RoomId);
        Assert.Equal(entry.TimeSlot.StartsAtUtc, dto.StartsAtUtc);
        Assert.Equal(entry.TimeSlot.EndsAtUtc, dto.EndsAtUtc);
        Assert.Equal(WaitlistStatus.Pending, dto.Status);
    }

    [Fact]
    public void JsonSerialization_EnumsSerializeAsStringsWithConverter()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var status = BookingStatus.CheckedIn;
        var json = JsonSerializer.Serialize(status, options);

        Assert.Equal("\"CheckedIn\"", json);
    }
}
