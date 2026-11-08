namespace Studium_Booking.Domain.Exceptions;

public class DomainException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
