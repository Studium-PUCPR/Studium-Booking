namespace Studium_Booking.Services.DTOs;

/// <summary>
/// Payload dispatched synchronously to the Studium-Notifications microservice.
/// </summary>
public record CreateNotificationRequest(
    string UserId,
    string Type,
    string Title,
    string Message,
    object? Data = null);
