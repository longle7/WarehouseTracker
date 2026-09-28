using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SensorDashboard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metadata");

            migrationBuilder.CreateTable(
                name: "Warehouses",
                schema: "metadata",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sensors",
                schema: "metadata",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    WarehouseId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MinTemperatureF = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxTemperatureF = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sensors", x => x.Id);
                    table.CheckConstraint("CK_Sensors_TemperatureRange", "[MinTemperatureF] < [MaxTemperatureF]");
                    table.ForeignKey(
                        name: "FK_Sensors_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "metadata",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "metadata",
                table: "Warehouses",
                columns: new[] { "Id", "City", "Name" },
                values: new object[,]
                {
                    { "WH-BOI", "Boise, ID", "Boise Fulfillment Hub" },
                    { "WH-PDX", "Portland, OR", "Portland Cold Storage" },
                    { "WH-SEA", "Seattle, WA", "Seattle Distribution Center" }
                });

            migrationBuilder.InsertData(
                schema: "metadata",
                table: "Sensors",
                columns: new[] { "Id", "IsActive", "Location", "MaxTemperatureF", "MinTemperatureF", "WarehouseId" },
                values: new object[,]
                {
                    { "WH-BOI-FRG-01", true, "Beverage Cooler", 40m, 34m, "WH-BOI" },
                    { "WH-BOI-FRG-02", true, "Dairy Cooler", 40m, 34m, "WH-BOI" },
                    { "WH-BOI-FRG-03", true, "Produce Cooler", 40m, 34m, "WH-BOI" },
                    { "WH-BOI-FRG-04", true, "Floral Fridge", 40m, 34m, "WH-BOI" },
                    { "WH-BOI-FRG-05", true, "Returns Fridge", 40m, 34m, "WH-BOI" },
                    { "WH-PDX-FRG-01", true, "Walk-in Cooler A", 40m, 34m, "WH-PDX" },
                    { "WH-PDX-FRG-02", true, "Walk-in Cooler B", 40m, 34m, "WH-PDX" },
                    { "WH-PDX-FRG-03", true, "Pharma Fridge", 40m, 34m, "WH-PDX" },
                    { "WH-SEA-FRG-01", true, "Dairy Cooler", 40m, 34m, "WH-SEA" },
                    { "WH-SEA-FRG-02", true, "Produce Cooler", 40m, 34m, "WH-SEA" },
                    { "WH-SEA-FRG-03", true, "Meat Locker", 40m, 34m, "WH-SEA" },
                    { "WH-SEA-FRG-04", true, "Loading Dock Fridge", 40m, 34m, "WH-SEA" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sensors_WarehouseId",
                schema: "metadata",
                table: "Sensors",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sensors",
                schema: "metadata");

            migrationBuilder.DropTable(
                name: "Warehouses",
                schema: "metadata");
        }
    }
}
