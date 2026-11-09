using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studium_Booking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_ExclusionConstraint_And_BlockedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenaltyRecords");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "BlockedUsers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedUsers", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedUsers_IsBlocked",
                table: "BlockedUsers",
                column: "IsBlocked");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(@"
                ALTER TABLE ""Bookings""
                ADD CONSTRAINT ""CK_Bookings_NoDoubleBooking""
                EXCLUDE USING gist (
                    ""RoomId"" WITH =,
                    tsrange(""StartsAtUtc"", ""EndsAtUtc"") WITH &&
                )
                WHERE (""Status"" IN (0, 1));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Bookings"" DROP CONSTRAINT IF EXISTS ""CK_Bookings_NoDoubleBooking"";");

            migrationBuilder.DropTable(
                name: "BlockedUsers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "PenaltyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyRecords_IsActive",
                table: "PenaltyRecords",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyRecords_Type",
                table: "PenaltyRecords",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyRecords_UserId",
                table: "PenaltyRecords",
                column: "UserId");
        }
    }
}
