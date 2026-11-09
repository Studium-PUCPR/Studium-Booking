using Studium_Booking.Domain.Entities.Users;
using Xunit;

namespace Studium_Booking.Tests.Domain;

public class BlockedUserTests
{
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BlockedUser_UpdateStatus_ShouldUpdateProperties()
    {
        var user = new BlockedUser("user-100", true, "Suspended for 5 no-shows", _now);

        Assert.Equal("user-100", user.UserId);
        Assert.True(user.IsBlocked);
        Assert.Equal("Suspended for 5 no-shows", user.BlockReason);

        user.UpdateStatus(false, null, _now.AddDays(7));
        Assert.False(user.IsBlocked);
        Assert.Null(user.BlockReason);
        Assert.Equal(_now.AddDays(7), user.UpdatedAtUtc);
    }
}
