using IoTDigitalTwin.Contracts.Metadata;

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

    /// <summary>The refrigerated unit the sensor is mounted in; drives its 3D model.</summary>
    public UnitType UnitType { get; set; } = UnitType.ReachInFridge;

    /// <summary>Unit center on the warehouse floor, in meters from the floor's corner.</summary>
    public double PositionX { get; set; }

    public double PositionZ { get; set; }

    /// <summary>Turn about the vertical axis; 0 means the door faces +Z.</summary>
    public double RotationDegrees { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
}
