using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Api.Middleware;

public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx && pgEx.SqlState == "23P01")
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                error = "BOOKING_CONFLICT",
                message = "A concurrent reservation was committed for this room during the selected interval."
            }, cancellationToken);
            return true;
        }

        if (exception is not DomainException domainException)
            return false;

        httpContext.Response.StatusCode = domainException switch
        {
            EntityNotFoundException => StatusCodes.Status404NotFound,
            BookingConflictException => StatusCodes.Status409Conflict,
            UserBlockedException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        await httpContext.Response.WriteAsJsonAsync(new
        {
            error = domainException.ErrorCode,
            message = domainException.Message
        }, cancellationToken);

        return true;
    }
}
