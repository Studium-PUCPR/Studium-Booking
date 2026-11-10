using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Inbox;
using Studium_Booking.Domain.Entities.Users;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Services.Clients;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;
using Wolverine;

namespace Studium_Booking.Services.Handlers;

/// <summary>
/// Subscribes to user status changes to maintain the local BlockedUsers read model for reservation eligibility,
/// and cascades cancellations on active upcoming reservations when a user is suspended.
/// </summary>
public class UserEligibilitySyncHandler(
    BookingDbContext db,
    IMessageBus messageBus,
    INotificationsClient notificationsClient,
    IOptions<BookingOptions> options,
    ILogger<UserEligibilitySyncHandler> logger)
{
    private static DateTime _lastPrunedUtc = DateTime.MinValue;
    private static readonly object _pruneLock = new();

    public async Task HandleAsync(UserStatusChangedEvent message, CancellationToken ct = default)
    {
        if (message.EventId.HasValue)
        {
            var alreadyProcessed = await db.InboxMessages.AnyAsync(m => m.Id == message.EventId.Value, ct);
            if (alreadyProcessed)
            {
                logger.LogWarning("Inbox: UserStatusChangedEvent {EventId} for user {UserId} already processed. Skipping.",
                    message.EventId.Value, message.UserId);
                return;
            }
        }
        var user = await db.BlockedUsers.FirstOrDefaultAsync(u => u.UserId == message.UserId, ct);
        if (user is null)
        {
            user = new BlockedUser(message.UserId, message.IsBlocked, message.BlockReason, message.OccurredAtUtc);
            await db.BlockedUsers.AddAsync(user, ct);
        }
        else
        {
            user.UpdateStatus(message.IsBlocked, message.BlockReason, message.OccurredAtUtc);
        }

        if (message.IsBlocked)
        {
            var upcomingBookings = await db.Bookings
                .Where(b => b.UserId == message.UserId &&
                            b.Status == BookingStatus.Confirmed &&
                            b.TimeSlot.EndsAtUtc > message.OccurredAtUtc)
                .ToListAsync(ct);

            foreach (var booking in upcomingBookings)
            {
                booking.Cancel($"User suspended: {message.BlockReason}", message.OccurredAtUtc, "System", isAdmin: true);

                await messageBus.PublishAsync(new AuditIntegrationEvent(
                    ServiceSource: "Booking",
                    ResourceId: booking.Id.ToString(),
                    ActorId: "System",
                    Action: "BookingCancelledDueToUserSuspension",
                    MetadataJson: $"{{\"reason\":\"{message.BlockReason}\"}}",
                    TimestampUtc: message.OccurredAtUtc));

                await notificationsClient.SendAsync(new CreateNotificationRequest(
                    UserId: message.UserId,
                    Type: "RESERVATION_CANCELLED",
                    Title: "Reserva Cancelada por Suspensão",
                    Message: $"Sua reserva '{booking.Title}' foi cancelada porque sua conta está suspensa: {message.BlockReason}",
                    Data: new { bookingId = booking.Id, roomId = booking.RoomId }), ct);

                // Promote next waitlist candidate for the freed slot
                var nextWaitlist = await db.WaitlistEntries
                    .Where(w => w.RoomId == booking.RoomId &&
                                w.Status == WaitlistStatus.Pending &&
                                w.TimeSlot.StartsAtUtc < booking.TimeSlot.EndsAtUtc &&
                                w.TimeSlot.EndsAtUtc > booking.TimeSlot.StartsAtUtc)
                    .OrderBy(w => w.CreatedAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (nextWaitlist is not null)
                {
                    nextWaitlist.Notify(message.OccurredAtUtc);

                    await messageBus.ScheduleAsync(
                        new CheckWaitlistClaimDeadlineMessage(nextWaitlist.Id),
                        TimeSpan.FromMinutes(options.Value.WaitlistClaimGraceMinutes));

                    await notificationsClient.SendAsync(new CreateNotificationRequest(
                        UserId: nextWaitlist.UserId,
                        Type: "WAITLIST_SLOT_AVAILABLE",
                        Title: "Vaga Disponível na Fila de Espera",
                        Message: $"Uma vaga foi liberada na sala {booking.RoomId}. Acesse a plataforma dentro de {options.Value.WaitlistClaimGraceMinutes} minutos para confirmar sua reserva.",
                        Data: new { roomId = booking.RoomId, waitlistId = nextWaitlist.Id }), ct);

                    logger.LogInformation("Promoted waitlist entry {WaitlistId} after suspended user {UserId} reservation cancelled.",
                        nextWaitlist.Id, message.UserId);
                }
            }
        }

        if (message.EventId.HasValue)
        {
            db.InboxMessages.Add(new InboxMessage(message.EventId.Value, nameof(UserEligibilitySyncHandler), DateTime.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Updated BlockedUser record for User {UserId}: IsBlocked={IsBlocked}, Reason={Reason}",
            message.UserId, message.IsBlocked, message.BlockReason);

        // Throttled lazy cleanup of inbox records older than 7 days (runs once every 6 hours)
        if (DateTime.UtcNow - _lastPrunedUtc > TimeSpan.FromHours(6))
        {
            lock (_pruneLock)
            {
                if (DateTime.UtcNow - _lastPrunedUtc > TimeSpan.FromHours(6))
                {
                    _lastPrunedUtc = DateTime.UtcNow;
                    var cutoff = DateTime.UtcNow.AddDays(-7);
                    _ = db.InboxMessages
                        .Where(m => m.ReceivedAtUtc < cutoff)
                        .ExecuteDeleteAsync(CancellationToken.None);
                }
            }
        }
    }
}
