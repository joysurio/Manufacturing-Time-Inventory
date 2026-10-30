using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManufacturingTimeTracking.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VariantComponentId",
                table: "OrderMaterialPickLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MachineVariantComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineVariantId = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineVariantComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineVariantComponents_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineVariantComponents_MachineVariants_MachineVariantId",
                        column: x => x.MachineVariantId,
                        principalTable: "MachineVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MachineVariantComponents_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineVariantComponentAlternatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComponentId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineVariantComponentAlternatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineVariantComponentAlternatives_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineVariantComponentAlternatives_MachineVariantComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "MachineVariantComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineVariantComponentAlternatives_ComponentId",
                table: "MachineVariantComponentAlternatives",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineVariantComponentAlternatives_ItemId",
                table: "MachineVariantComponentAlternatives",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineVariantComponents_ItemId",
                table: "MachineVariantComponents",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineVariantComponents_MachineVariantId",
                table: "MachineVariantComponents",
                column: "MachineVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineVariantComponents_MaterialId",
                table: "MachineVariantComponents",
                column: "MaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineVariantComponentAlternatives");

            migrationBuilder.DropTable(
                name: "MachineVariantComponents");

            migrationBuilder.DropColumn(
                name: "VariantComponentId",
                table: "OrderMaterialPickLines");
        }
    }
}
