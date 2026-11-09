using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Services.Clients;

/// <summary>
/// Synchronous REST client for dispatching immediate notifications to Studium-Notifications.
/// </summary>
public interface INotificationsClient
{
    Task SendAsync(CreateNotificationRequest request, CancellationToken ct = default);
}
