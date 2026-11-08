using Studium_Booking.Domain.Entities.Penalties;
using Studium_Booking.Domain.Exceptions;
using Xunit;

namespace Studium_Booking.Tests.Domain;

public class PenaltyTests
{
    private const string UserId = "user-999";
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Suspension_ShouldBeActiveAndCurrentlyBlocking()
    {
        var penalty = PenaltyRecord.Create(UserId, PenaltyType.Suspension, "Repeated No-Show", _now);

        Assert.Equal(PenaltyType.Suspension, penalty.Type);
        Assert.True(penalty.IsActive);
        Assert.True(penalty.IsCurrentlyBlocking(_now.AddDays(1)));
        Assert.False(penalty.IsCurrentlyBlocking(_now.AddDays(8))); // Expired after 7 days
    }

    [Fact]
    public void Revoke_ActivePenalty_ShouldDeactivateAndNoLongerBlock()
    {
        var penalty = PenaltyRecord.Create(UserId, PenaltyType.Suspension, "Repeated No-Show", _now);
        penalty.Revoke("Pardoned by Administrator", _now.AddHours(2));

        Assert.False(penalty.IsActive);
        Assert.Equal("Pardoned by Administrator", penalty.RevokedReason);
        Assert.False(penalty.IsCurrentlyBlocking(_now.AddHours(3)));
    }

    [Fact]
    public void Revoke_AlreadyInactivePenalty_ShouldThrowDomainException()
    {
        var penalty = PenaltyRecord.Create(UserId, PenaltyType.NoShowInfraction, "No-Show", _now);
        penalty.Revoke("Justified reason", _now.AddHours(1));

        var ex = Assert.Throws<DomainException>(() =>
            penalty.Revoke("Second revocation attempt", _now.AddHours(2)));

        Assert.Equal("PENALTY_ALREADY_INACTIVE", ex.ErrorCode);
    }
}
