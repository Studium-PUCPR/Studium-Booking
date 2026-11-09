using Studium_Booking.Domain.Common;

namespace Studium_Booking.Domain.Entities.Users;

public class BlockedUser
{
    public string UserId { get; init; } = default!;
    public bool IsBlocked { get; private set; }
    public string? BlockReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private BlockedUser() { }

    public BlockedUser(string userId, bool isBlocked, string? reason, DateTime nowUtc)
    {
        UserId = userId.Trim();
        IsBlocked = isBlocked;
        BlockReason = reason?.Trim();
        UpdatedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void UpdateStatus(bool isBlocked, string? reason, DateTime nowUtc)
    {
        IsBlocked = isBlocked;
        BlockReason = reason?.Trim();
        UpdatedAtUtc = UtcDateTime.Normalize(nowUtc);
    }
}
