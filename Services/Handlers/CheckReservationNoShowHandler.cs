using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;
using Wolverine;

namespace Studium_Booking.Services.Handlers;

public class CheckReservationNoShowHandler(
    BookingDbContext db,
    IMessageBus messageBus,
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
            // Reschedule if invoked prematurely
            var remainingDelay = graceDeadline - nowUtc;
            await messageBus.ScheduleAsync(new CheckReservationNoShowMessage(booking.Id), remainingDelay);
            return;
        }

        booking.MarkNoShow(nowUtc, options.Value.GraceMinutesAfterCheckIn);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BookingIntegrationEvent(
            BookingEventType.NoShowRecorded,
            booking.UserId,
            booking.Id.ToString(),
            nowUtc));

        await messageBus.PublishAsync(new ReservationNotificationEvent(
            "NoShowRecorded",
            booking.Id,
            booking.UserId,
            booking.RoomId,
            booking.TimeSlot.StartsAtUtc,
            booking.TimeSlot.EndsAtUtc,
            "Reservation marked as No-Show due to unconfirmed attendance.",
            nowUtc));

        // Check if waitlist has candidates for newly freed slot
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

            await messageBus.PublishAsync(new WaitlistSlotAvailableEvent(
                booking.RoomId,
                nextWaitlist.Id,
                nextWaitlist.UserId,
                booking.TimeSlot.StartsAtUtc,
                booking.TimeSlot.EndsAtUtc,
                nowUtc));
        }

        logger.LogWarning("Reservation {BookingId} automatically marked as No-Show. NoShowRecorded event dispatched.",
            booking.Id);
    }
}
