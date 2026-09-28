namespace SensorDashboard.Api.Data.Metadata;

public sealed class Warehouse
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string City { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Floor size in meters, for the 3D scene.</summary>
    public double FloorWidthM { get; set; } = 40;

    public double FloorDepthM { get; set; } = 25;

    public List<Sensor> Sensors { get; } = [];
}
