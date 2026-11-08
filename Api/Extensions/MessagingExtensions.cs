using Amazon;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Handlers;
using Wolverine;
using Wolverine.AmazonSqs;

namespace Studium_Booking.Api.Extensions;

public static class MessagingExtensions
{
    public static void AddStudiumMessaging(this WebApplicationBuilder builder)
    {
        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(CatalogEventsHandler).Assembly);

            var awsRegion = builder.Configuration["AWS:Region"] ?? "us-east-2";
            opts.UseAmazonSqsTransport(cfg =>
            {
                cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(awsRegion);
                var serviceUrl = builder.Configuration["AWS:ServiceURL"];
                if (!string.IsNullOrWhiteSpace(serviceUrl))
                    cfg.ServiceURL = serviceUrl;
            }).AutoProvision();

            var bookingQueueName = builder.Configuration["AWS:SQS:BookingQueueName"];
            if (!string.IsNullOrWhiteSpace(bookingQueueName))
            {
                opts.ListenToSqsQueue(bookingQueueName);
            }

            var usersQueueName = builder.Configuration["AWS:SQS:UsersQueueName"];
            if (!string.IsNullOrWhiteSpace(usersQueueName))
            {
                opts.PublishMessage<BookingIntegrationEvent>().ToSqsQueue(usersQueueName);
            }

            var notificationsQueueName = builder.Configuration["AWS:SQS:NotificationsQueueName"];
            if (!string.IsNullOrWhiteSpace(notificationsQueueName))
            {
                opts.PublishMessage<ReservationNotificationEvent>().ToSqsQueue(notificationsQueueName);
                opts.PublishMessage<WaitlistSlotAvailableEvent>().ToSqsQueue(notificationsQueueName);
            }
        });
    }
}
