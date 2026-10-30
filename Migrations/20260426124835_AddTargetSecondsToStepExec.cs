using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManufacturingTimeTracking.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetSecondsToStepExec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetSeconds",
                table: "StepExecs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetSeconds",
                table: "StepExecs");
        }
    }
}
