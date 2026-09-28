using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DigitalTwinScene : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FloorDepthM",
                schema: "metadata",
                table: "Warehouses",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "FloorWidthM",
                schema: "metadata",
                table: "Warehouses",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                schema: "metadata",
                table: "Sensors",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PositionZ",
                schema: "metadata",
                table: "Sensors",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RotationDegrees",
                schema: "metadata",
                table: "Sensors",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "UnitType",
                schema: "metadata",
                table: "Sensors",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-BOI-FRG-01",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 10.0, 26.0, 180.0, "DisplayCase" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-BOI-FRG-02",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 8.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-BOI-FRG-03",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 16.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-BOI-FRG-04",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 20.0, 26.0, 180.0, "DisplayCase" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-BOI-FRG-05",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 48.0, 16.0, 270.0, "ReachInFridge" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-PDX-FRG-01",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 6.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-PDX-FRG-02",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 14.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-PDX-FRG-03",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 28.0, 12.0, 270.0, "ReachInFridge" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-SEA-FRG-01",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 6.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-SEA-FRG-02",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 14.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-SEA-FRG-03",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 22.0, 3.0, 0.0, "WalkInCooler" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Sensors",
                keyColumn: "Id",
                keyValue: "WH-SEA-FRG-04",
                columns: new[] { "PositionX", "PositionZ", "RotationDegrees", "UnitType" },
                values: new object[] { 36.0, 22.0, 180.0, "ReachInFridge" });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-BOI",
                columns: new[] { "FloorDepthM", "FloorWidthM" },
                values: new object[] { 30.0, 50.0 });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-PDX",
                columns: new[] { "FloorDepthM", "FloorWidthM" },
                values: new object[] { 20.0, 30.0 });

            migrationBuilder.UpdateData(
                schema: "metadata",
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: "WH-SEA",
                columns: new[] { "FloorDepthM", "FloorWidthM" },
                values: new object[] { 25.0, 40.0 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Warehouses_FloorSize",
                schema: "metadata",
                table: "Warehouses",
                sql: "[FloorWidthM] > 0 AND [FloorDepthM] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sensors_Position",
                schema: "metadata",
                table: "Sensors",
                sql: "[PositionX] >= 0 AND [PositionZ] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Warehouses_FloorSize",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sensors_Position",
                schema: "metadata",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "FloorDepthM",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "FloorWidthM",
                schema: "metadata",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "PositionX",
                schema: "metadata",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "PositionZ",
                schema: "metadata",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "RotationDegrees",
                schema: "metadata",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "UnitType",
                schema: "metadata",
                table: "Sensors");
        }
    }
}
