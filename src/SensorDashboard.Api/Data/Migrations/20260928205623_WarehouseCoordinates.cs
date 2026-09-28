using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                schema: "metadata",
                table: "Warehouses",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                schema: "metadata",
                table: "Warehouses",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-BOI",
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { 43.615000000000002, -116.20229999999999 });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-PDX",
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { 45.5152, -122.6784 });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-SEA",
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { 47.606200000000001, -122.3321 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Warehouses_Latitude",
                schema: "metadata",
                table: "Warehouses",
                sql: "[Latitude] BETWEEN -90 AND 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Warehouses_Longitude",
                schema: "metadata",
                table: "Warehouses",
                sql: "[Longitude] BETWEEN -180 AND 180");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Warehouses_Latitude",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Warehouses_Longitude",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "metadata",
                table: "Warehouses");
        }
    }
}
