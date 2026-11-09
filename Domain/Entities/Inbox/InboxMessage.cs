namespace Studium_Booking.Domain.Entities.Inbox;

/// <summary>
/// Persists unique integration event IDs consumed by Studium-Booking to guarantee exactly-once processing (Inbox Pattern).
/// </summary>
public class InboxMessage(Guid id, string handlerName, DateTime receivedAtUtc)
{
    public Guid Id { get; init; } = id;
    public string HandlerName { get; init; } = handlerName;
    public DateTime ReceivedAtUtc { get; init; } = receivedAtUtc;
}
