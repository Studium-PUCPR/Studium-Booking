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
        var client = httpClientFactory.CreateClient(HttpClientName);
        const int maxAttempts = 3;
        var delays = new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000) };

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await client.PostAsJsonAsync("/api/notifications", request, ct);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Attempt {Attempt} to deliver synchronous notification to user {UserId} failed. Retrying in {Delay}ms...",
                    attempt, request.UserId, delays[attempt - 1].TotalMilliseconds);
                await Task.Delay(delays[attempt - 1], ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Final attempt {Attempt} to deliver synchronous notification to user {UserId} failed", attempt, request.UserId);
            }
        }
    }
}
