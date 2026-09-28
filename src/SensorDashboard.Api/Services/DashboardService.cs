using IoTDigitalTwin.Contracts.Metadata;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Metadata;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Services;

/// <summary>
/// Read side for the dashboard: joins metadata (EF Core) with telemetry (IReadingQueries)
/// in memory, the way a Lambda would combine DynamoDB and Timestream results.
/// </summary>
public sealed class DashboardService(
    DigitalTwinDbContext db,
    IReadingQueries readings,
    IOptions<DashboardOptions> options,
    TimeProvider timeProvider)
{
    // Bucket sizes a chart axis reads naturally.
    private static readonly TimeSpan[] NiceBuckets =
    [
        .. new[] { 1, 5, 10, 15, 30 }.Select(s => TimeSpan.FromSeconds(s)),
        .. new[] { 1, 5, 10, 15, 30 }.Select(m => TimeSpan.FromMinutes(m)),
        .. new[] { 1, 3, 6, 12, 24 }.Select(h => TimeSpan.FromHours(h)),
    ];

    private DashboardOptions Options => options.Value;

    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Include(w => w.Sensors)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        var statuses = await GetStatusesAsync(warehouses.SelectMany(w => w.Sensors).ToList(), cancellationToken);

        return warehouses
            .Select(w => new WarehouseDto(
                w.Id,
                w.Name,
                w.City,
                w.Latitude,
                w.Longitude,
                SensorCount: w.Sensors.Count,
                AlertCount: w.Sensors.Count(s => statuses[s.Id].Status == SensorStatus.Alert),
                OfflineCount: w.Sensors.Count(s => statuses[s.Id].Status == SensorStatus.Offline)))
            .ToList();
    }

    /// <returns>null if the warehouse doesn't exist.</returns>
    public async Task<IReadOnlyList<SensorDto>?> GetSensorsAsync(string warehouseId, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Include(w => w.Sensors.OrderBy(s => s.Id))
            .SingleOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        var statuses = await GetStatusesAsync(warehouse.Sensors, cancellationToken);

        return warehouse.Sensors
            .Select(s => new SensorDto(
                s.Id,
                s.WarehouseId,
                s.Location,
                s.MinTemperatureF,
                s.MaxTemperatureF,
                s.IsActive,
                statuses[s.Id].Status,
                statuses[s.Id].Last is { } r
                    ? new LastReadingDto(r.Timestamp, r.Temperature, r.Humidity, r.DoorOpen, r.IsAnomaly)
                    : null))
            .ToList();
    }

    /// <returns>null if the sensor doesn't exist.</returns>
    public async Task<IReadOnlyList<SensorPropertyDto>?> GetPropertiesAsync(
        string sensorId, HistoryRange range, CancellationToken cancellationToken)
    {
        var exists = await db.Sensors.AnyAsync(s => s.Id == sensorId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var buckets = await readings.GetHistoryAsync(sensorId, range.From, range.To, range.Bucket, cancellationToken);

        return
        [
            new("temperature", "°F", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.AvgTemperature, 2), b.MinTemperature, b.MaxTemperature)).ToList()),
            new("humidity", "%", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.AvgHumidity, 1), b.MinHumidity, b.MaxHumidity)).ToList()),
            new("doorOpen", "ratio", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.DoorOpenRatio, 3))).ToList()),
            new("anomalies", "count", buckets.Select(b => new PropertyValueDto(b.BucketStart, b.AnomalyCount)).ToList()),
        ];
    }

    /// <summary>
    /// Fills in defaults and validates a history request. Returns an error message instead of
    /// a range when the request is invalid.
    /// </summary>
    public (HistoryRange? Range, string? Error) ResolveRange(DateTimeOffset? from, DateTimeOffset? to, int? bucketSeconds)
    {
        var end = to ?? timeProvider.GetUtcNow();
        var start = from ?? end - Options.DefaultHistoryWindow;
        var window = end - start;

        if (window <= TimeSpan.Zero)
            return (null, "'from' must be earlier than 'to'.");
        if (window > Options.MaxHistoryWindow)
            return (null, $"The range can't exceed {Options.MaxHistoryWindow.TotalDays:0} days.");
        if (bucketSeconds is < 1 or > 86_400)
            return (null, "'bucketSeconds' must be between 1 and 86400.");

        var bucket = bucketSeconds is { } s
            ? TimeSpan.FromSeconds(s)
            : NiceBuckets.FirstOrDefault(b => window / b <= Options.TargetHistoryPoints, NiceBuckets[^1]);

        return (new HistoryRange(start, end, bucket), null);
    }

    private async Task<Dictionary<string, (SensorStatus Status, LatestReading? Last)>> GetStatusesAsync(
        IReadOnlyCollection<Sensor> sensors, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var latest = await readings.GetLatestAsync(
            sensors.Select(s => s.Id).ToList(), now - Options.LastReadingLookback, cancellationToken);

        return sensors.ToDictionary(s => s.Id, s =>
        {
            var last = latest.GetValueOrDefault(s.Id);
            return (Evaluate(s, last, now), last);
        });
    }

    private SensorStatus Evaluate(Sensor sensor, LatestReading? last, DateTimeOffset now) => sensor switch
    {
        { IsActive: false } => SensorStatus.Inactive,
        _ when last is null || now - last.Timestamp > Options.OfflineAfter => SensorStatus.Offline,
        _ when last.IsAnomaly
            || last.Temperature < (double)sensor.MinTemperatureF
            || last.Temperature > (double)sensor.MaxTemperatureF => SensorStatus.Alert,
        _ => SensorStatus.Ok,
    };
}

public sealed record HistoryRange(DateTimeOffset From, DateTimeOffset To, TimeSpan Bucket);
