using SensorDashboard.Api.Data.Metadata;

namespace SensorDashboard.Api.Data;

/// <summary>
/// Seed topology. IDs must match SensorSimulator's WarehouseTopology, otherwise
/// ingestion rejects the readings as coming from unknown sensors.
/// </summary>
internal static class MetadataSeed
{
    public static Warehouse[] Warehouses { get; } =
    [
        new() { Id = "WH-SEA", Name = "Seattle Distribution Center", City = "Seattle, WA" },
        new() { Id = "WH-PDX", Name = "Portland Cold Storage", City = "Portland, OR" },
        new() { Id = "WH-BOI", Name = "Boise Fulfillment Hub", City = "Boise, ID" },
    ];

    public static Sensor[] Sensors { get; } =
    [
        .. Create("WH-SEA", "Dairy Cooler", "Produce Cooler", "Meat Locker", "Loading Dock Fridge"),
        .. Create("WH-PDX", "Walk-in Cooler A", "Walk-in Cooler B", "Pharma Fridge"),
        .. Create("WH-BOI", "Beverage Cooler", "Dairy Cooler", "Produce Cooler", "Floral Fridge", "Returns Fridge"),
    ];

    private static IEnumerable<Sensor> Create(string warehouseId, params string[] locations) =>
        locations.Select((location, i) => new Sensor
        {
            Id = $"{warehouseId}-FRG-{i + 1:D2}",
            WarehouseId = warehouseId,
            Location = location,
        });
}
