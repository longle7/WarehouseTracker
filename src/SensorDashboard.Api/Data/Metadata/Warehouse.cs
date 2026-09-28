namespace SensorDashboard.Api.Data.Metadata;

public sealed class Warehouse
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string City { get; set; }

    public List<Sensor> Sensors { get; } = [];
}
