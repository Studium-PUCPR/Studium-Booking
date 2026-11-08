using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Studium_Booking.Data;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Services.DTOs;

namespace Studium_Booking.Api.Controllers;

[ApiController]
[Route("api/bookings/reports")]
[Authorize]
public class ReportsController(BookingDbContext db) : ControllerBase
{
    [HttpGet("occupancy")]
    public async Task<ActionResult<OccupancyReportDto>> GetOccupancyReport(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] Guid? roomId,
        CancellationToken ct)
    {
        var normalizedFrom = fromUtc.ToUniversalTime();
        var normalizedTo = toUtc.ToUniversalTime();

        var query = db.Bookings.AsNoTracking().Where(
            b => b.TimeSlot.StartsAtUtc < normalizedTo && b.TimeSlot.EndsAtUtc > normalizedFrom);

        if (roomId.HasValue)
            query = query.Where(b => b.RoomId == roomId.Value);

        var bookings = await query.ToListAsync(ct);

        var total = bookings.Count;
        var confirmed = bookings.Count(b => b.Status == BookingStatus.Confirmed);
        var checkedIn = bookings.Count(b => b.Status == BookingStatus.CheckedIn);
        var completed = bookings.Count(b => b.Status == BookingStatus.Completed);
        var cancelled = bookings.Count(b => b.Status == BookingStatus.Cancelled);
        var noShow = bookings.Count(b => b.Status == BookingStatus.NoShow);

        var checkInRate = total > 0 ? Math.Round((double)(checkedIn + completed) / total * 100.0, 2) : 0.0;
        var cancellationRate = total > 0 ? Math.Round((double)cancelled / total * 100.0, 2) : 0.0;
        var noShowRate = total > 0 ? Math.Round((double)noShow / total * 100.0, 2) : 0.0;

        var totalHours = Math.Round(bookings.Sum(b => b.TimeSlot.Duration.TotalHours), 2);

        var peakHours = bookings
            .GroupBy(b => b.TimeSlot.StartsAtUtc.Hour)
            .Select(g => new HourlyOccupancyDto(g.Key, g.Count()))
            .OrderBy(h => h.HourOfDay)
            .ToList();

        var roomBreakdown = bookings
            .GroupBy(b => b.RoomId)
            .Select(g => new RoomOccupancyDto(
                g.Key,
                g.Count(),
                Math.Round(g.Sum(b => b.TimeSlot.Duration.TotalHours), 2)))
            .OrderByDescending(r => r.TotalBookings)
            .ToList();

        return Ok(new OccupancyReportDto(
            normalizedFrom,
            normalizedTo,
            total,
            confirmed,
            checkedIn,
            completed,
            cancelled,
            noShow,
            checkInRate,
            cancellationRate,
            noShowRate,
            totalHours,
            peakHours,
            roomBreakdown));
    }
}
