namespace SensorDashboard.Api.Data.Metadata;

public sealed class Sensor
{
    public required string Id { get; set; }

    public required string WarehouseId { get; set; }

    /// <summary>Where the sensor sits inside the warehouse, e.g. "Dairy Cooler".</summary>
    public required string Location { get; set; }

    /// <summary>Lower bound of the safe range, in degrees Fahrenheit.</summary>
    public decimal MinTemperatureF { get; set; } = 34;

    /// <summary>Upper bound of the safe range, in degrees Fahrenheit.</summary>
    public decimal MaxTemperatureF { get; set; } = 40;

    /// <summary>Readings from inactive sensors are rejected at ingestion.</summary>
    public bool IsActive { get; set; } = true;

    public Warehouse Warehouse { get; set; } = null!;
}
