namespace Studium_Booking.Domain.Exceptions;

public class BookingConflictException(string message) : DomainException("BOOKING_CONFLICT", message);
