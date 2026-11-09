using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Handlers;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Studium_Booking.Api.Extensions;

public static class MessagingExtensions
{
    public static void AddStudiumMessaging(this WebApplicationBuilder builder)
    {
        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(CatalogEventsHandler).Assembly);

            var rabbitConnectionString = builder.Configuration["RabbitMQ:ConnectionString"]
                ?? builder.Configuration["RABBITMQ_URL"]
                ?? "amqp://studium:StudiumRabbitMQ2026!@studium-rabbitmq-service.studium.svc.cluster.local:5672";

            opts.UseRabbitMq(new Uri(rabbitConnectionString)).AutoProvision();

            var bookingQueueName = builder.Configuration["RabbitMQ:BookingQueueName"] ?? "studium-booking-events-queue";
            opts.ListenToRabbitQueue(bookingQueueName);

            var usersQueueName = builder.Configuration["RabbitMQ:UsersQueueName"] ?? "studium-users-events-queue";
            opts.PublishMessage<BookingIntegrationEvent>().ToRabbitQueue(usersQueueName);

            var notificationsQueueName = builder.Configuration["RabbitMQ:NotificationsQueueName"] ?? "studium-notifications-queue";
            opts.PublishMessage<ReservationNotificationEvent>().ToRabbitQueue(notificationsQueueName);
            opts.PublishMessage<WaitlistSlotAvailableEvent>().ToRabbitQueue(notificationsQueueName);
        });
    }
}
