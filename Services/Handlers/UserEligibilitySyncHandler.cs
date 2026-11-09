using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Users;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Services.Handlers;

/// <summary>
/// Subscribes to user status changes to maintain the local BlockedUsers read model for reservation eligibility.
/// </summary>
public class UserEligibilitySyncHandler(
    BookingDbContext db,
    ILogger<UserEligibilitySyncHandler> logger)
{
    public async Task HandleAsync(UserStatusChangedEvent message, CancellationToken ct = default)
    {
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

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Updated BlockedUser record for User {UserId}: IsBlocked={IsBlocked}, Reason={Reason}",
            message.UserId, message.IsBlocked, message.BlockReason);
    }
}
