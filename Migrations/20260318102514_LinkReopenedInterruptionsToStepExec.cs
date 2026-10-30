using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManufacturingTimeTracking.Migrations
{
    /// <inheritdoc />
    public partial class LinkReopenedInterruptionsToStepExec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StepExecId",
                table: "ProductionInterruptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInterruptions_StepExecId",
                table: "ProductionInterruptions",
                column: "StepExecId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionInterruptions_StepExecs_StepExecId",
                table: "ProductionInterruptions",
                column: "StepExecId",
                principalTable: "StepExecs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionInterruptions_StepExecs_StepExecId",
                table: "ProductionInterruptions");

            migrationBuilder.DropIndex(
                name: "IX_ProductionInterruptions_StepExecId",
                table: "ProductionInterruptions");

            migrationBuilder.DropColumn(
                name: "StepExecId",
                table: "ProductionInterruptions");
        }
    }
}
