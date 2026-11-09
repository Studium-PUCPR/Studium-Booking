using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Services.Clients;

/// <summary>
/// Typed HTTP client communicating synchronously with the Studium-Notifications microservice.
/// </summary>
public class NotificationsClient(IHttpClientFactory httpClientFactory, ILogger<NotificationsClient> logger) : INotificationsClient
{
    public const string HttpClientName = "NotificationsClient";

    public async Task SendAsync(CreateNotificationRequest request, CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync("/api/notifications", request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver synchronous notification to user {UserId}", request.UserId);
        }
    }
}
