using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Studium_Booking.Api.Extensions;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Penalties;
using Studium_Booking.Domain.Exceptions;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Api.Controllers;

[ApiController]
[Route("api/bookings/penalties")]
[Authorize]
public class PenaltiesController(BookingDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PenaltyResponseDto>>> GetPenalties(
        [FromQuery] string? userId,
        [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        var currentUserId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var query = db.Penalties.AsNoTracking().AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(p => p.UserId == currentUserId);
        }
        else if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(p => p.UserId == userId.Trim());
        }

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);

        var items = await query
            .OrderByDescending(p => p.IssuedAtUtc)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<PenaltyResponseDto>> RevokePenalty(
        Guid id,
        [FromBody] RevokePenaltyRequest request,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var penalty = await db.Penalties.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (penalty is null)
            throw new EntityNotFoundException("PENALTY_NOT_FOUND", $"Penalty record '{id}' was not found.");

        penalty.Revoke(request.Reason, nowUtc);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(penalty));
    }

    private static PenaltyResponseDto ToDto(PenaltyRecord p) =>
        new(
            p.Id,
            p.UserId,
            p.BookingId,
            p.Type,
            p.Reason,
            p.IssuedAtUtc,
            p.ExpiresAtUtc,
            p.IsActive,
            p.RevokedReason,
            p.RevokedAtUtc);
}
