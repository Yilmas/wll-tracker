using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dockhound.Migrations
{
    /// <inheritdoc />
    public partial class AddWarAndVerificationRecordWarNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarNumber",
                table: "VerificationRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Wars",
                columns: table => new
                {
                    WarId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WarNumber = table.Column<int>(type: "int", nullable: false),
                    Winner = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ConquestStartTime = table.Column<long>(type: "bigint", nullable: false),
                    ConquestEndTime = table.Column<long>(type: "bigint", nullable: true),
                    ResistanceStartTime = table.Column<long>(type: "bigint", nullable: true),
                    ScheduledConquestEndTime = table.Column<long>(type: "bigint", nullable: true),
                    RequiredVictoryTowns = table.Column<int>(type: "int", nullable: false),
                    ShortRequiredVictoryTowns = table.Column<int>(type: "int", nullable: false),
                    EntityTag = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RefreshAfterUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wars", x => x.WarId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wars_ConquestStartTime",
                table: "Wars",
                column: "ConquestStartTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wars");

            migrationBuilder.DropColumn(
                name: "WarNumber",
                table: "VerificationRecords");
        }
    }
}
