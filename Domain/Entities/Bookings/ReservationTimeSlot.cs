using Studium_Booking.Domain.Common;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Domain.Entities.Bookings;

public sealed record ReservationTimeSlot
{
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }

    // Required for EF Core deserialization
    private ReservationTimeSlot() { }

    public ReservationTimeSlot(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var start = UtcDateTime.Normalize(startsAtUtc);
        var end = UtcDateTime.Normalize(endsAtUtc);

        if (start >= end)
            throw new DomainException("INVALID_TIME_SLOT", "Reservation start time must be strictly before end time.");

        var duration = end - start;
        if (duration < TimeSpan.FromMinutes(15))
            throw new DomainException("DURATION_TOO_SHORT", "Reservation duration must be at least 15 minutes.");

        if (duration > TimeSpan.FromHours(12))
            throw new DomainException("DURATION_EXCEEDED", "Reservation duration cannot exceed 12 hours.");

        StartsAtUtc = start;
        EndsAtUtc = end;
    }

    public TimeSpan Duration => EndsAtUtc - StartsAtUtc;

    public bool Overlaps(ReservationTimeSlot other) =>
        StartsAtUtc < other.EndsAtUtc && EndsAtUtc > other.StartsAtUtc;

    public bool Overlaps(DateTime startUtc, DateTime endUtc) =>
        StartsAtUtc < endUtc && EndsAtUtc > startUtc;
}
