using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManufacturingTimeTracking.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyTimeEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyTimeEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EvalDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScoreEntry = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ScoreExit = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ScoreTasks = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ScoreWorked = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ScoreBreak = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AverageScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    EntryLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExitLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TaskPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    WorkedSeconds = table.Column<int>(type: "int", nullable: false),
                    BreakSeconds = table.Column<int>(type: "int", nullable: false),
                    TaskMinutes = table.Column<int>(type: "int", nullable: false),
                    MinutesInFactory = table.Column<int>(type: "int", nullable: false),
                    SummaryHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTimeEvaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTimeEvaluations_UserId_EvalDateUtc",
                table: "DailyTimeEvaluations",
                columns: new[] { "UserId", "EvalDateUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyTimeEvaluations");
        }
    }
}
