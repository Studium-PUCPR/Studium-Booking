using Studium_Booking.Domain.Common;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Domain.Entities.Waitlist;

public class WaitlistEntry
{
    public Guid Id { get; init; }
    public Guid RoomId { get; init; }
    public string UserId { get; init; } = default!;
    public ReservationTimeSlot TimeSlot { get; private set; } = default!;
    public WaitlistStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? NotifiedAtUtc { get; private set; }

    private WaitlistEntry() { }

    public static WaitlistEntry Create(
        Guid roomId,
        string userId,
        ReservationTimeSlot timeSlot,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("INVALID_USER_ID", "User identifier cannot be empty.");

        var normalizedNow = UtcDateTime.Normalize(nowUtc);

        return new WaitlistEntry
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            UserId = userId.Trim(),
            TimeSlot = timeSlot,
            Status = WaitlistStatus.Pending,
            CreatedAtUtc = normalizedNow
        };
    }

    public void Notify(DateTime nowUtc)
    {
        if (Status != WaitlistStatus.Pending)
            throw new DomainException("INVALID_STATUS", $"Cannot notify waitlist entry with status '{Status}'. Only Pending entries can be notified.");

        Status = WaitlistStatus.Notified;
        NotifiedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void Promote(DateTime nowUtc)
    {
        if (Status != WaitlistStatus.Pending && Status != WaitlistStatus.Notified)
            throw new DomainException("INVALID_STATUS", $"Cannot promote waitlist entry with status '{Status}'.");

        Status = WaitlistStatus.Promoted;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status != WaitlistStatus.Pending && Status != WaitlistStatus.Notified)
            throw new DomainException("INVALID_STATUS", $"Cannot cancel waitlist entry with status '{Status}'.");

        Status = WaitlistStatus.Cancelled;
    }

    public void Expire(DateTime nowUtc)
    {
        if (Status == WaitlistStatus.Pending || Status == WaitlistStatus.Notified)
        {
            Status = WaitlistStatus.Expired;
        }
    }
}
