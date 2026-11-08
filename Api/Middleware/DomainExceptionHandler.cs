using Microsoft.AspNetCore.Diagnostics;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Api.Middleware;

public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
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
