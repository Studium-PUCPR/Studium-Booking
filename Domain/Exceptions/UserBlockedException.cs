namespace Studium_Booking.Domain.Exceptions;

public class UserBlockedException(string message) : DomainException("USER_BLOCKED", message);
