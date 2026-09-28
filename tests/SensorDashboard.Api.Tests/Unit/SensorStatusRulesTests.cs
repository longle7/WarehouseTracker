using IoTDigitalTwin.Contracts.Metadata;
using SensorDashboard.Api.Data.Metadata;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Tests.Unit;

public class SensorStatusRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(20);

    private static Sensor Sensor(bool active = true) => new()
    {
        Id = "S1", WarehouseId = "W1", Location = "Test", MinTemperatureF = 34, MaxTemperatureF = 40, IsActive = active,
    };

    private static LatestReading Reading(double temperature = 37, bool anomaly = false, double ageSeconds = 1) =>
        new("S1", Now.AddSeconds(-ageSeconds), temperature, 50, DoorOpen: false, anomaly);

    [Fact]
    public void Inactive_wins_over_everything() =>
        Assert.Equal(SensorStatus.Inactive, SensorStatusRules.Evaluate(Sensor(active: false), Reading(99), Now, OfflineAfter));

    [Fact]
    public void No_reading_is_offline() =>
        Assert.Equal(SensorStatus.Offline, SensorStatusRules.Evaluate(Sensor(), null, Now, OfflineAfter));

    [Fact]
    public void Stale_reading_is_offline_even_if_it_was_alerting() =>
        Assert.Equal(SensorStatus.Offline, SensorStatusRules.Evaluate(Sensor(), Reading(99, ageSeconds: 21), Now, OfflineAfter));

    [Theory]
    [InlineData(33.9)]
    [InlineData(40.1)]
    public void Out_of_range_is_alert(double temperature) =>
        Assert.Equal(SensorStatus.Alert, SensorStatusRules.Evaluate(Sensor(), Reading(temperature), Now, OfflineAfter));

    [Fact]
    public void Flagged_anomaly_is_alert_even_in_range() =>
        Assert.Equal(SensorStatus.Alert, SensorStatusRules.Evaluate(Sensor(), Reading(37, anomaly: true), Now, OfflineAfter));

    [Theory]
    [InlineData(34)]
    [InlineData(37)]
    [InlineData(40)]
    public void In_range_including_the_bounds_is_ok(double temperature) =>
        Assert.Equal(SensorStatus.Ok, SensorStatusRules.Evaluate(Sensor(), Reading(temperature, ageSeconds: 20), Now, OfflineAfter));
}
