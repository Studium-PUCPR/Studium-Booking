namespace Studium_Booking.Domain.Entities.ReplicatedCatalog;

public class ReplicatedOperatingHours
{
    public Guid Id { get; init; }
    public Guid RoomId { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public TimeOnly OpensAt { get; init; }
    public TimeOnly ClosesAt { get; init; }

    private ReplicatedOperatingHours() { }

    public ReplicatedOperatingHours(Guid id, Guid roomId, DayOfWeek dayOfWeek, TimeOnly opensAt, TimeOnly closesAt)
    {
        Id = id;
        RoomId = roomId;
        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }
}
