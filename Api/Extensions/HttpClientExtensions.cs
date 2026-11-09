using Studium_Booking.Services.Clients;

namespace Studium_Booking.Api.Extensions;

public static class HttpClientExtensions
{
    public static void AddNotificationsClient(this IServiceCollection services, IConfiguration configuration)
    {
        var notificationsUrl = configuration["Services:NotificationsUrl"]
            ?? configuration["NOTIFICATIONS_SERVICE_URL"]
            ?? "http://studium-notifications-service.studium.svc.cluster.local";

        services.AddHttpClient(NotificationsClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(notificationsUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddScoped<INotificationsClient, NotificationsClient>();
    }
}
