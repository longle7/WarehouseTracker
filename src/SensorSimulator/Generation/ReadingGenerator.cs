using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;
using SensorSimulator.Topology;

namespace SensorSimulator.Generation;

public enum AnomalyKind
{
    /// <summary>Temperature jumps well above range (e.g. compressor failure).</summary>
    TemperatureSpike,

    /// <summary>Door reports open continuously and the fridge warms up.</summary>
    DoorStuckOpen,

    /// <summary>The sensor stops reporting entirely.</summary>
    Outage,
}

/// <summary>
/// Produces one reading per sensor per tick. Each sensor does a mean-reverting random walk
/// inside the normal range; anomalies start with <see cref="SimulatorOptions.AnomalyProbability"/>
/// and last a random number of ticks. Not thread-safe; owned by the single worker loop.
/// </summary>
public sealed class ReadingGenerator
{
    private const double TargetHumidity = 50;
    private const double MeanReversion = 0.2;
    private const double NormalDoorOpenProbability = 0.05;

    private readonly SimulatorOptions _options;
    private readonly ILogger<ReadingGenerator> _logger;
    private readonly Random _random;
    private readonly List<SensorState> _sensors;
    private readonly double _targetTemperature;

    public ReadingGenerator(IReadOnlyList<Warehouse> warehouses, IOptions<SimulatorOptions> options, ILogger<ReadingGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;
        _random = _options.RandomSeed is { } seed ? new Random(seed) : new Random();
        _targetTemperature = (_options.MinTemperatureF + _options.MaxTemperatureF) / 2;
        _sensors = warehouses
            .SelectMany(w => w.Sensors)
            .Select(s => new SensorState(s, _targetTemperature + NextGaussian(0, 0.5), TargetHumidity + NextGaussian(0, 3)))
            .ToList();
    }

    public IReadOnlyList<SensorReadingDto> NextTick(DateTimeOffset timestamp)
    {
        var readings = new List<SensorReadingDto>(_sensors.Count);

        foreach (var state in _sensors)
        {
            MaybeStartAnomaly(state);

            if (Next(state, timestamp) is { } reading)
            {
                readings.Add(reading);
            }

            EndAnomalyIfExpired(state);
        }

        return readings;
    }

    private SensorReadingDto? Next(SensorState state, DateTimeOffset timestamp)
    {
        var anomaly = state.Anomaly;
        if (anomaly == AnomalyKind.Outage)
        {
            return null;
        }

        var doorOpen = anomaly == AnomalyKind.DoorStuckOpen || _random.NextDouble() < NormalDoorOpenProbability;

        // Pull back toward target, plus noise; an open door warms the fridge and lets humid air in.
        state.Temperature += (_targetTemperature - state.Temperature) * MeanReversion + NextGaussian(0, 0.3);
        state.Humidity += (TargetHumidity - state.Humidity) * MeanReversion + NextGaussian(0, 1);
        if (doorOpen)
        {
            state.Temperature += anomaly == AnomalyKind.DoorStuckOpen ? 1.5 + _random.NextDouble() : 0.3;
            state.Humidity += 2;
        }

        var temperature = state.Temperature + (anomaly == AnomalyKind.TemperatureSpike ? state.SpikeOffset : 0);
        var outOfRange = temperature < _options.MinTemperatureF || temperature > _options.MaxTemperatureF;

        return new SensorReadingDto
        {
            SensorId = state.Sensor.Id,
            WarehouseId = state.Sensor.WarehouseId,
            Timestamp = timestamp,
            Temperature = Math.Round(temperature, 2),
            Humidity = Math.Round(Math.Clamp(state.Humidity, 0, 100), 1),
            DoorOpen = doorOpen,
            // Ground truth: an injected anomaly, or still out of range while recovering from one.
            IsAnomaly = anomaly is not null || outOfRange,
        };
    }

    private void MaybeStartAnomaly(SensorState state)
    {
        if (state.Anomaly is not null || _random.NextDouble() >= _options.AnomalyProbability)
        {
            return;
        }

        var kinds = Enum.GetValues<AnomalyKind>();
        state.Anomaly = kinds[_random.Next(kinds.Length)];
        state.AnomalyTicksRemaining = _random.Next(_options.MinAnomalyTicks, _options.MaxAnomalyTicks + 1);
        state.SpikeOffset = 8 + _random.NextDouble() * 7;

        _logger.LogInformation(
            "Anomaly {Kind} started on {SensorId} ({WarehouseId}) for {Ticks} ticks",
            state.Anomaly, state.Sensor.Id, state.Sensor.WarehouseId, state.AnomalyTicksRemaining);
    }

    private void EndAnomalyIfExpired(SensorState state)
    {
        if (state.Anomaly is null || --state.AnomalyTicksRemaining > 0)
        {
            return;
        }

        _logger.LogInformation("Anomaly {Kind} ended on {SensorId}", state.Anomaly, state.Sensor.Id);
        state.Anomaly = null;
    }

    // Box-Muller transform.
    private double NextGaussian(double mean, double stdDev)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private sealed class SensorState(Sensor sensor, double temperature, double humidity)
    {
        public Sensor Sensor { get; } = sensor;
        public double Temperature { get; set; } = temperature;
        public double Humidity { get; set; } = humidity;
        public AnomalyKind? Anomaly { get; set; }
        public int AnomalyTicksRemaining { get; set; }
        public double SpikeOffset { get; set; }
    }
}
