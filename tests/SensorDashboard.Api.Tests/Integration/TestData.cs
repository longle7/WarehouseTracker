using IoTDigitalTwin.Contracts.Telemetry;

namespace SensorDashboard.Api.Tests.Integration;

internal static class TestData
{
    // Seeded sensors (see MetadataSeed).
    public const string SeaDairy = "WH-SEA-FRG-01";
    public const string SeaProduce = "WH-SEA-FRG-02";
    public const string Seattle = "WH-SEA";

    public static SensorReadingDto Reading(
        string sensorId,
        DateTimeOffset timestamp,
        double temperature = 37,
        double humidity = 50,
        bool doorOpen = false,
        bool isAnomaly = false,
        string warehouseId = Seattle) =>
        new()
        {
            SensorId = sensorId,
            WarehouseId = warehouseId,
            Timestamp = timestamp,
            Temperature = temperature,
            Humidity = humidity,
            DoorOpen = doorOpen,
            IsAnomaly = isAnomaly,
        };

    /// <summary>Now, truncated to whole seconds so round-trips through datetime2(3) compare equal.</summary>
    public static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }
}
