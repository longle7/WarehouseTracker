using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Metadata;

namespace SensorDashboard.Api.Services;

/// <summary>
/// Warehouses and sensors change rarely but were re-read on every broadcast, alert evaluation
/// and request. This keeps them in memory for a short time; a single-flight load means a burst
/// of cache misses costs one query. The cached entities are shared, so treat them as read-only.
/// </summary>
public sealed class MetadataCache(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private const string Key = "metadata:warehouses";
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _loading = new(1, 1);

    /// <summary>All warehouses by name, each with its sensors by ID.</summary>
    public async Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key, out IReadOnlyList<Warehouse>? cached)) return cached!;

        await _loading.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(Key, out cached)) return cached!; // loaded while we waited

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DigitalTwinDbContext>();
            IReadOnlyList<Warehouse> warehouses = await db.Warehouses
                .AsNoTracking()
                .Include(w => w.Sensors.OrderBy(s => s.Id))
                .OrderBy(w => w.Name)
                .ToListAsync(cancellationToken);
            return cache.Set(Key, warehouses, Ttl);
        }
        finally
        {
            _loading.Release();
        }
    }

    public async Task<Warehouse?> FindWarehouseAsync(string id, CancellationToken cancellationToken) =>
        (await GetWarehousesAsync(cancellationToken)).FirstOrDefault(w => w.Id == id);

    public async Task<IReadOnlyList<Sensor>> GetSensorsAsync(CancellationToken cancellationToken) =>
        (await GetWarehousesAsync(cancellationToken)).SelectMany(w => w.Sensors).ToList();

    public async Task<bool> SensorExistsAsync(string id, CancellationToken cancellationToken) =>
        (await GetWarehousesAsync(cancellationToken)).Any(w => w.Sensors.Any(s => s.Id == id));

    /// <summary>Call after changing warehouses or sensors so the next read reloads them.</summary>
    public void Invalidate() => cache.Remove(Key);
}
