using Studium_Booking.Domain.Common;

namespace Studium_Booking.Domain.Entities.ReplicatedCatalog;

public class ReplicatedMaintenanceBlock
{
    public Guid Id { get; init; }
    public Guid RoomId { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public string Reason { get; init; } = default!;

    private ReplicatedMaintenanceBlock() { }

    public ReplicatedMaintenanceBlock(Guid id, Guid roomId, DateTime startsAtUtc, DateTime endsAtUtc, string reason)
    {
        Id = id;
        RoomId = roomId;
        StartsAtUtc = UtcDateTime.Normalize(startsAtUtc);
        EndsAtUtc = UtcDateTime.Normalize(endsAtUtc);
        Reason = reason;
    }

    public bool Overlaps(DateTime startUtc, DateTime endUtc) =>
        StartsAtUtc < endUtc && EndsAtUtc > startUtc;
}
