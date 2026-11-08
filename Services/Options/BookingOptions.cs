namespace Studium_Booking.Services.Options;

public class BookingOptions
{
    public const string SectionName = "Booking";

    public int GraceMinutesBeforeCheckIn { get; set; } = 15;
    public int GraceMinutesAfterCheckIn { get; set; } = 15;
    public string DefaultTimeZone { get; set; } = "America/Sao_Paulo";
    public int MaxNoShowsBeforeSuspension { get; set; } = 3;
    public int DefaultSuspensionDays { get; set; } = 7;
}
