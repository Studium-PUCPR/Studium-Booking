using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studium_Booking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Booking_Composite_Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Bookings_Room_Status_TimeSlot""
                ON ""Bookings"" (""RoomId"", ""Status"", ""StartsAtUtc"", ""EndsAtUtc"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_Bookings_Room_Status_TimeSlot"";
            ");
        }
    }
}
