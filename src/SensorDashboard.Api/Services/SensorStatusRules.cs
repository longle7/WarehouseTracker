using IoTDigitalTwin.Contracts.Metadata;
using SensorDashboard.Api.Data.Metadata;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Services;

/// <summary>Instantaneous sensor status from its metadata and latest reading.</summary>
public static class SensorStatusRules
{
    public static SensorStatus Evaluate(Sensor sensor, LatestReading? last, DateTimeOffset now, TimeSpan offlineAfter) =>
        sensor switch
        {
            { IsActive: false } => SensorStatus.Inactive,
            _ when last is null || now - last.Timestamp > offlineAfter => SensorStatus.Offline,
            _ when last.IsAnomaly || IsOutOfRange(sensor, last.Temperature) => SensorStatus.Alert,
            _ => SensorStatus.Ok,
        };

    public static bool IsOutOfRange(Sensor sensor, double temperature) =>
        temperature < (double)sensor.MinTemperatureF || temperature > (double)sensor.MaxTemperatureF;
}
