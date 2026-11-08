namespace Studium_Booking.Domain.Exceptions;

public class EntityNotFoundException(string errorCode, string message) : DomainException(errorCode, message);
