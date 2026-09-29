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
    private DashboardOptions Options => options.Value;

    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken) =>
        (await GetSnapshotAsync(cancellationToken)).Warehouses;

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
        return warehouse.Sensors.Select(s => ToSensorDto(s, statuses[s.Id])).ToList();
    }

    /// <summary>Static 3D layout for a warehouse; null if it doesn't exist.</summary>
    public async Task<WarehouseSceneDto?> GetSceneAsync(string warehouseId, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Include(w => w.Sensors.OrderBy(s => s.Id))
            .SingleOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);

        return warehouse is null
            ? null
            : new WarehouseSceneDto(
                warehouse.Id,
                warehouse.FloorWidthM,
                warehouse.FloorDepthM,
                warehouse.Sensors.Select(s =>
                {
                    var (width, depth, height) = UnitDimensions.For(s.UnitType);
                    return new SceneUnitDto(
                        s.Id, s.Location, s.UnitType, s.PositionX, s.PositionZ, s.RotationDegrees, width, depth, height);
                }).ToList());
    }

    /// <summary>
    /// Every warehouse summary and every warehouse's sensor list, from one metadata load and
    /// one latest-reading query. Used by the live broadcaster; same shapes as the GET endpoints.
    /// </summary>
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Include(w => w.Sensors.OrderBy(s => s.Id))
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        var statuses = await GetStatusesAsync(warehouses.SelectMany(w => w.Sensors).ToList(), cancellationToken);

        return new DashboardSnapshot(
            warehouses.Select(w => new WarehouseDto(
                    w.Id,
                    w.Name,
                    w.City,
                    w.Latitude,
                    w.Longitude,
                    SensorCount: w.Sensors.Count,
                    AlertCount: w.Sensors.Count(s => statuses[s.Id].Status == SensorStatus.Alert),
                    OfflineCount: w.Sensors.Count(s => statuses[s.Id].Status == SensorStatus.Offline)))
                .ToList(),
            warehouses.ToDictionary(
                w => w.Id,
                w => (IReadOnlyList<SensorDto>)w.Sensors.Select(s => ToSensorDto(s, statuses[s.Id])).ToList()));
    }

    private static SensorDto ToSensorDto(Sensor s, (SensorStatus Status, LatestReading? Last) state) =>
        new(
            s.Id,
            s.WarehouseId,
            s.Location,
            s.MinTemperatureF,
            s.MaxTemperatureF,
            s.IsActive,
            state.Status,
            state.Last is { } r ? new LastReadingDto(r.Timestamp, r.Temperature, r.Humidity, r.DoorOpen, r.IsAnomaly) : null);

    /// <returns>null if the sensor doesn't exist.</returns>
    public async Task<SensorHistory?> GetPropertiesAsync(
        string sensorId, HistoryRange range, CancellationToken cancellationToken)
    {
        var exists = await db.Sensors.AnyAsync(s => s.Id == sensorId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var buckets = await readings.GetHistoryAsync(sensorId, range.From, range.To, range.Bucket, cancellationToken);

        IReadOnlyList<SensorPropertyDto> properties =
        [
            new("temperature", "°F", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.AvgTemperature, 2), b.ReadingCount, b.MinTemperature, b.MaxTemperature)).ToList()),
            new("humidity", "%", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.AvgHumidity, 1), b.ReadingCount, b.MinHumidity, b.MaxHumidity)).ToList()),
            new("doorOpen", "ratio", buckets.Select(b => new PropertyValueDto(b.BucketStart, Math.Round(b.DoorOpenRatio, 3), b.ReadingCount)).ToList()),
            new("anomalies", "count", buckets.Select(b => new PropertyValueDto(b.BucketStart, b.AnomalyCount, b.ReadingCount)).ToList()),
        ];
        var lastReadingAt = buckets.Count == 0 ? null : buckets.Max(b => b.LastReadingAt);
        return new SensorHistory(properties, lastReadingAt);
    }

    public (HistoryRange? Range, string? Error) ResolveRange(DateTimeOffset? from, DateTimeOffset? to, int? bucketSeconds) =>
        HistoryRangeResolver.Resolve(from, to, bucketSeconds, timeProvider.GetUtcNow(), Options);

    private async Task<Dictionary<string, (SensorStatus Status, LatestReading? Last)>> GetStatusesAsync(
        IReadOnlyCollection<Sensor> sensors, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var latest = await readings.GetLatestAsync(
            sensors.Select(s => s.Id).ToList(), now - Options.LastReadingLookback, cancellationToken);

        return sensors.ToDictionary(s => s.Id, s =>
        {
            var last = latest.GetValueOrDefault(s.Id);
            return (SensorStatusRules.Evaluate(s, last, now, Options.OfflineAfter), last);
        });
    }
}

/// <param name="LastReadingAt">Newest reading included (raw-backed history only; null from rollups).</param>
public sealed record SensorHistory(IReadOnlyList<SensorPropertyDto> Properties, DateTimeOffset? LastReadingAt);

public sealed record DashboardSnapshot(
    IReadOnlyList<WarehouseDto> Warehouses,
    IReadOnlyDictionary<string, IReadOnlyList<SensorDto>> SensorsByWarehouse);
