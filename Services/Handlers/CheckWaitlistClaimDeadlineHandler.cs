using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Services.Clients;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;
using Wolverine;

namespace Studium_Booking.Services.Handlers;

/// <summary>
/// Evaluates whether a notified waitlist candidate claimed their promoted slot within the grace deadline.
/// If expired, expires the candidate and automatically promotes the next eligible student in queue.
/// </summary>
public class CheckWaitlistClaimDeadlineHandler(
    BookingDbContext db,
    IMessageBus messageBus,
    INotificationsClient notificationsClient,
    IOptions<BookingOptions> options,
    ILogger<CheckWaitlistClaimDeadlineHandler> logger)
{
    public async Task HandleAsync(CheckWaitlistClaimDeadlineMessage message, CancellationToken ct = default)
    {
        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == message.WaitlistId, ct);
        if (entry is null)
        {
            logger.LogInformation("Waitlist deadline timer: Entry {WaitlistId} was not found.", message.WaitlistId);
            return;
        }

        if (entry.Status != WaitlistStatus.Notified)
        {
            logger.LogInformation("Waitlist deadline timer: Entry {WaitlistId} status is {Status}. Expiration evaluation skipped.",
                entry.Id, entry.Status);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var deadline = (entry.NotifiedAtUtc ?? entry.CreatedAtUtc).AddMinutes(options.Value.WaitlistClaimGraceMinutes);

        if (nowUtc < deadline)
        {
            var remaining = deadline - nowUtc;
            await messageBus.ScheduleAsync(new CheckWaitlistClaimDeadlineMessage(entry.Id), remaining);
            return;
        }

        entry.Expire(nowUtc);
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Waitlist candidate {WaitlistId} for Room {RoomId} expired without claiming slot.",
            entry.Id, entry.RoomId);

        var nextWaitlist = await db.WaitlistEntries
            .Where(w => w.RoomId == entry.RoomId &&
                        w.Status == WaitlistStatus.Pending &&
                        w.TimeSlot.StartsAtUtc < entry.TimeSlot.EndsAtUtc &&
                        w.TimeSlot.EndsAtUtc > entry.TimeSlot.StartsAtUtc)
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
                Message: $"Uma vaga foi liberada na sala {nextWaitlist.RoomId}. Acesse a plataforma dentro de {options.Value.WaitlistClaimGraceMinutes} minutos para confirmar sua reserva.",
                Data: new { roomId = nextWaitlist.RoomId, waitlistId = nextWaitlist.Id }), ct);

            logger.LogInformation("Promoted next waitlist candidate {NextWaitlistId} for Room {RoomId}.",
                nextWaitlist.Id, nextWaitlist.RoomId);
        }
    }
}
