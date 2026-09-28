using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;
using SensorSimulator.Generation;
using SensorSimulator.Topology;

namespace SensorSimulator.Tests;

public class ReadingGeneratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly int SensorCount = WarehouseTopology.Seed.Sum(w => w.Sensors.Count);

    private static ReadingGenerator Create(double anomalyProbability, int seed = 42) =>
        new(
            WarehouseTopology.Seed,
            Options.Create(new SimulatorOptions { AnomalyProbability = anomalyProbability, RandomSeed = seed }),
            NullLogger<ReadingGenerator>.Instance);

    private static List<IReadOnlyList<SensorReadingDto>> Run(ReadingGenerator generator, int ticks) =>
        Enumerable.Range(0, ticks).Select(i => generator.NextTick(Start.AddSeconds(5 * i))).ToList();

    [Fact]
    public void Without_anomalies_every_sensor_reports_in_range_every_tick()
    {
        var ticks = Run(Create(anomalyProbability: 0), 500);

        Assert.All(ticks, tick =>
        {
            Assert.Equal(SensorCount, tick.Count);
            Assert.Equal(SensorCount, tick.Select(r => r.SensorId).Distinct().Count());
        });
        var readings = ticks.SelectMany(t => t).ToList();
        Assert.All(readings, r =>
        {
            Assert.InRange(r.Temperature, 34, 40);
            Assert.InRange(r.Humidity, 0, 100);
            Assert.False(r.IsAnomaly);
        });
        // Doors open now and then during normal operation.
        Assert.Contains(readings, r => r.DoorOpen);
    }

    [Fact]
    public void Readings_in_a_tick_share_its_timestamp_and_carry_the_sensors_warehouse()
    {
        var tick = Create(anomalyProbability: 0).NextTick(Start);
        var warehouseBySensor = WarehouseTopology.Seed.SelectMany(w => w.Sensors).ToDictionary(s => s.Id, s => s.WarehouseId);

        Assert.All(tick, r =>
        {
            Assert.Equal(Start, r.Timestamp);
            Assert.Equal(warehouseBySensor[r.SensorId], r.WarehouseId);
        });
    }

    [Fact]
    public void Same_seed_produces_the_same_sequence()
    {
        var first = Run(Create(anomalyProbability: 0.1, seed: 7), 100).SelectMany(t => t).ToList();
        var second = Run(Create(anomalyProbability: 0.1, seed: 7), 100).SelectMany(t => t).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Certain_anomalies_flag_every_reported_reading_and_outages_drop_sensors()
    {
        // With probability 1 every sensor starts an anomaly on the first tick. Outages report
        // nothing; spikes and stuck doors report flagged readings.
        var tick = Create(anomalyProbability: 1).NextTick(Start);

        Assert.All(tick, r => Assert.True(r.IsAnomaly));
        Assert.True(tick.Count < SensorCount, "Expected at least one sensor in an outage with this seed.");
    }

    [Fact]
    public void Anomalies_end_and_readings_return_to_range()
    {
        var generator = Create(anomalyProbability: 0.05, seed: 3);
        var ticks = Run(generator, 400);

        // Anomalies happened...
        Assert.Contains(ticks.SelectMany(t => t), r => r.IsAnomaly);
        // ...and every sensor is back to normal readings at some later point.
        var lastTicks = ticks.TakeLast(50).SelectMany(t => t).ToList();
        Assert.All(WarehouseTopology.Seed.SelectMany(w => w.Sensors), sensor =>
            Assert.Contains(lastTicks, r => r.SensorId == sensor.Id && !r.IsAnomaly));
    }
}
