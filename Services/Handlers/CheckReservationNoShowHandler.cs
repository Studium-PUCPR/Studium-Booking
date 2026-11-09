using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Services.Clients;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;
using Wolverine;

namespace Studium_Booking.Services.Handlers;

public class CheckReservationNoShowHandler(
    BookingDbContext db,
    IMessageBus messageBus,
    INotificationsClient notificationsClient,
    IOptions<BookingOptions> options,
    ILogger<CheckReservationNoShowHandler> logger)
{
    public async Task HandleAsync(CheckReservationNoShowMessage message, CancellationToken ct = default)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == message.BookingId, ct);
        if (booking is null)
        {
            logger.LogInformation("No-show timer: Booking {BookingId} no longer exists.", message.BookingId);
            return;
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            logger.LogInformation("No-show timer: Booking {BookingId} status is {Status}. No-Show evaluation skipped.",
                booking.Id, booking.Status);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var graceDeadline = booking.TimeSlot.StartsAtUtc.AddMinutes(options.Value.GraceMinutesAfterCheckIn);

        if (nowUtc < graceDeadline)
        {
            var remainingDelay = graceDeadline - nowUtc;
            await messageBus.ScheduleAsync(new CheckReservationNoShowMessage(booking.Id), remainingDelay);
            return;
        }

        booking.MarkNoShow(nowUtc, options.Value.GraceMinutesAfterCheckIn);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new UserActivityIntegrationEvent(
            UserActivityType.NoShowRecorded,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc));

        await messageBus.PublishAsync(new AuditIntegrationEvent(
            ServiceSource: "Booking",
            ResourceId: booking.Id.ToString(),
            ActorId: "System",
            Action: "NoShowRecorded",
            MetadataJson: $"{{\"reason\":\"Check-in grace period expired\",\"roomId\":\"{booking.RoomId}\"}}",
            TimestampUtc: nowUtc));

        await notificationsClient.SendAsync(new CreateNotificationRequest(
            UserId: booking.UserId,
            Type: "NO_SHOW_RECORDED",
            Title: "Prazo de Presença Expirado",
            Message: $"Sua reserva na sala {booking.RoomId} foi cancelada por ausência de check-in dentro da tolerância de {options.Value.GraceMinutesAfterCheckIn} minutos.",
            Data: new { bookingId = booking.Id, roomId = booking.RoomId }), ct);

        var nextWaitlist = await db.WaitlistEntries
            .Where(w => w.RoomId == booking.RoomId &&
                        w.Status == WaitlistStatus.Pending &&
                        w.TimeSlot.StartsAtUtc < booking.TimeSlot.EndsAtUtc &&
                        w.TimeSlot.EndsAtUtc > booking.TimeSlot.StartsAtUtc)
            .OrderBy(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (nextWaitlist is not null)
        {
            nextWaitlist.Notify(nowUtc);
            await db.SaveChangesAsync(ct);

            await messageBus.ScheduleAsync(
                new CheckWaitlistClaimDeadlineMessage(nextWaitlist.Id),
                TimeSpan.FromMinutes(options.Value.WaitlistClaimGraceMinutes));

            await notificationsClient.SendAsync(new CreateNotificationRequest(
                UserId: nextWaitlist.UserId,
                Type: "WAITLIST_SLOT_AVAILABLE",
                Title: "Vaga Disponível na Fila de Espera",
                Message: $"Uma vaga foi liberada na sala {booking.RoomId}. Acesse a plataforma dentro de {options.Value.WaitlistClaimGraceMinutes} minutos para confirmar sua reserva.",
                Data: new { roomId = booking.RoomId, waitlistId = nextWaitlist.Id }), ct);
        }

        logger.LogWarning("Reservation {BookingId} automatically marked as No-Show. NoShowRecorded event dispatched.",
            booking.Id);
    }
}
