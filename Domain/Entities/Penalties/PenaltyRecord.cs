using Studium_Booking.Domain.Common;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Domain.Entities.Penalties;

public class PenaltyRecord
{
    public Guid Id { get; init; }
    public string UserId { get; init; } = default!;
    public Guid? BookingId { get; init; }
    public PenaltyType Type { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime IssuedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public string? RevokedReason { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private PenaltyRecord() { }

    public static PenaltyRecord Create(
        string userId,
        PenaltyType type,
        string reason,
        DateTime nowUtc,
        Guid? bookingId = null,
        TimeSpan? suspensionDuration = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("INVALID_USER_ID", "User identifier cannot be empty.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("INVALID_PENALTY_REASON", "A penalty reason must be provided.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        DateTime? expiresAtUtc = null;

        if (suspensionDuration.HasValue)
        {
            expiresAtUtc = normalizedNow.Add(suspensionDuration.Value);
        }
        else if (type == PenaltyType.Suspension)
        {
            // Default 7 days suspension
            expiresAtUtc = normalizedNow.AddDays(7);
        }

        return new PenaltyRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId.Trim(),
            BookingId = bookingId,
            Type = type,
            Reason = reason.Trim(),
            IssuedAtUtc = normalizedNow,
            ExpiresAtUtc = expiresAtUtc,
            IsActive = true
        };
    }

    public void Revoke(string reason, DateTime nowUtc)
    {
        if (!IsActive)
            throw new DomainException("PENALTY_ALREADY_INACTIVE", "Penalty is already inactive or revoked.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("REVOCATION_REASON_REQUIRED", "Revocation reason must be specified.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        IsActive = false;
        RevokedReason = reason.Trim();
        RevokedAtUtc = normalizedNow;
    }

    public bool IsCurrentlyBlocking(DateTime nowUtc)
    {
        if (!IsActive) return false;
        if (Type != PenaltyType.Suspension) return false;
        if (ExpiresAtUtc.HasValue && nowUtc >= ExpiresAtUtc.Value) return false;

        return true;
    }
}
