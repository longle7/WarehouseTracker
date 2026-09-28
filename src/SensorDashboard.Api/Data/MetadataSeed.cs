using IoTDigitalTwin.Contracts.Metadata;
using SensorDashboard.Api.Data.Metadata;

namespace SensorDashboard.Api.Data;

/// <summary>
/// Seed topology. IDs must match SensorSimulator's WarehouseTopology, otherwise
/// ingestion rejects the readings as coming from unknown sensors. Positions are meters on
/// each warehouse floor (see <see cref="WarehouseSceneDto"/>) and must fit inside it.
/// </summary>
internal static class MetadataSeed
{
    public static Warehouse[] Warehouses { get; } =
    [
        new()
        {
            Id = "WH-SEA", Name = "Seattle Distribution Center", City = "Seattle, WA",
            Latitude = 47.6062, Longitude = -122.3321, FloorWidthM = 40, FloorDepthM = 25,
        },
        new()
        {
            Id = "WH-PDX", Name = "Portland Cold Storage", City = "Portland, OR",
            Latitude = 45.5152, Longitude = -122.6784, FloorWidthM = 30, FloorDepthM = 20,
        },
        new()
        {
            Id = "WH-BOI", Name = "Boise Fulfillment Hub", City = "Boise, ID",
            Latitude = 43.6150, Longitude = -116.2023, FloorWidthM = 50, FloorDepthM = 30,
        },
    ];

    public static Sensor[] Sensors { get; } =
    [
        // Walk-ins along the back wall (door facing +Z into the floor), dock fridge by the far corner.
        .. Create("WH-SEA",
            ("Dairy Cooler", UnitType.WalkInCooler, 6, 3, 0),
            ("Produce Cooler", UnitType.WalkInCooler, 14, 3, 0),
            ("Meat Locker", UnitType.WalkInCooler, 22, 3, 0),
            ("Loading Dock Fridge", UnitType.ReachInFridge, 36, 22, 180)),
        .. Create("WH-PDX",
            ("Walk-in Cooler A", UnitType.WalkInCooler, 6, 3, 0),
            ("Walk-in Cooler B", UnitType.WalkInCooler, 14, 3, 0),
            ("Pharma Fridge", UnitType.ReachInFridge, 28, 12, 270)),
        // Display cases along the front wall, opening facing back into the floor (-Z).
        .. Create("WH-BOI",
            ("Beverage Cooler", UnitType.DisplayCase, 10, 26, 180),
            ("Dairy Cooler", UnitType.WalkInCooler, 8, 3, 0),
            ("Produce Cooler", UnitType.WalkInCooler, 16, 3, 0),
            ("Floral Fridge", UnitType.DisplayCase, 20, 26, 180),
            ("Returns Fridge", UnitType.ReachInFridge, 48, 16, 270)),
    ];

    private static IEnumerable<Sensor> Create(
        string warehouseId,
        params (string Location, UnitType Type, double X, double Z, double Rotation)[] units) =>
        units.Select((u, i) => new Sensor
        {
            Id = $"{warehouseId}-FRG-{i + 1:D2}",
            WarehouseId = warehouseId,
            Location = u.Location,
            UnitType = u.Type,
            PositionX = u.X,
            PositionZ = u.Z,
            RotationDegrees = u.Rotation,
        });
}
