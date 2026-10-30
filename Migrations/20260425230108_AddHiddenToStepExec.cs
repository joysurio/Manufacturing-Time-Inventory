using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManufacturingTimeTracking.Migrations
{
    /// <inheritdoc />
    public partial class AddHiddenToStepExec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@" 
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'StepExecs')
                AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepExecs]') AND name = 'Hidden')
                BEGIN
                    ALTER TABLE [dbo].[StepExecs] ADD [Hidden] bit NOT NULL DEFAULT 0;
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepExecs]') AND name = 'Hidden')
                BEGIN
                    ALTER TABLE [dbo].[StepExecs] DROP COLUMN [Hidden];
                END");
        }
    }
}
