namespace SensorSimulator.Topology;

public sealed record Warehouse(string Id, string Name, IReadOnlyList<Sensor> Sensors);

public sealed record Sensor(string Id, string WarehouseId, string Location);

/// <summary>
/// In-memory seed of the simulated warehouses and their fridge sensors.
/// </summary>
public static class WarehouseTopology
{
    public static IReadOnlyList<Warehouse> Seed { get; } =
    [
        Create("WH-SEA", "Seattle Distribution Center", "Dairy Cooler", "Produce Cooler", "Meat Locker", "Loading Dock Fridge"),
        Create("WH-PDX", "Portland Cold Storage", "Walk-in Cooler A", "Walk-in Cooler B", "Pharma Fridge"),
        Create("WH-BOI", "Boise Fulfillment Hub", "Beverage Cooler", "Dairy Cooler", "Produce Cooler", "Floral Fridge", "Returns Fridge"),
    ];

    private static Warehouse Create(string warehouseId, string name, params string[] locations) =>
        new(
            warehouseId,
            name,
            locations.Select((location, i) => new Sensor($"{warehouseId}-FRG-{i + 1:D2}", warehouseId, location)).ToArray());
}
