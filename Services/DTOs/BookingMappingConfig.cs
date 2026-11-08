using Mapster;
using Studium_Booking.Domain.Entities.Bookings;
using Studium_Booking.Domain.Entities.Waitlist;

namespace Studium_Booking.Services.DTOs;

public static class BookingMappingConfig
{
    public static void Configure()
    {
        TypeAdapterConfig<Booking, BookingResponseDto>.NewConfig()
            .Map(dest => dest.StartsAtUtc, src => src.TimeSlot.StartsAtUtc)
            .Map(dest => dest.EndsAtUtc, src => src.TimeSlot.EndsAtUtc);

        TypeAdapterConfig<WaitlistEntry, WaitlistResponseDto>.NewConfig()
            .Map(dest => dest.StartsAtUtc, src => src.TimeSlot.StartsAtUtc)
            .Map(dest => dest.EndsAtUtc, src => src.TimeSlot.EndsAtUtc);
    }
}
