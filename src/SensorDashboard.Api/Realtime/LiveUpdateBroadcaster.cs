using System.Threading.Channels;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Realtime;

public interface ILiveUpdateNotifier
{
    /// <summary>Queues newly stored readings for broadcast. Never blocks the ingest request.</summary>
    void NotifyIngested(IReadOnlyList<SensorReadingDto> readings);
}

/// <summary>
/// Pushes dashboard state over SignalR. Wakes on each ingest (coalescing bursts into one
/// broadcast) or after <see cref="DashboardOptions.LiveHeartbeat"/> with no ingests, so
/// offline sensors still surface. Status is computed server-side by the same
/// <see cref="DashboardService"/> the GET endpoints use, so pushed and fetched data agree.
/// </summary>
public sealed class LiveUpdateBroadcaster(
    IServiceScopeFactory scopeFactory,
    IHubContext<TelemetryHub, ITelemetryClient> hub,
    LiveConnectionTracker connections,
    IOptions<DashboardOptions> options,
    ILogger<LiveUpdateBroadcaster> logger) : BackgroundService, ILiveUpdateNotifier
{
    // Bounded so a stalled broadcaster can't grow memory; dropping old batches is safe
    // because every broadcast sends full snapshots.
    private readonly Channel<IReadOnlyList<SensorReadingDto>> _ingested =
        Channel.CreateBounded<IReadOnlyList<SensorReadingDto>>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    public void NotifyIngested(IReadOnlyList<SensorReadingDto> readings) => _ingested.Writer.TryWrite(readings);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForIngestOrHeartbeatAsync(stoppingToken);

            var readings = new List<SensorReadingDto>();
            while (_ingested.Reader.TryRead(out var batch))
            {
                readings.AddRange(batch);
            }

            if (!connections.HasConnections)
            {
                continue;
            }

            try
            {
                await BroadcastAsync(readings, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Live update broadcast failed");
            }
        }
    }

    private async Task WaitForIngestOrHeartbeatAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(options.Value.LiveHeartbeat);
        try
        {
            await _ingested.Reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Heartbeat: no ingest within the interval.
        }
    }

    private async Task BroadcastAsync(List<SensorReadingDto> readings, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardService>();
        var snapshot = await dashboard.GetSnapshotAsync(cancellationToken);

        var sends = new List<Task> { hub.Clients.All.WarehousesUpdated(snapshot.Warehouses) };
        sends.AddRange(snapshot.SensorsByWarehouse.Select(kv =>
            hub.Clients.Group(TelemetryHub.WarehouseGroup(kv.Key)).SensorsUpdated(kv.Key, kv.Value)));
        sends.AddRange(readings.GroupBy(r => r.SensorId).Select(g =>
            hub.Clients.Group(TelemetryHub.SensorGroup(g.Key)).ReadingsIngested(g.Key, g.ToList())));

        await Task.WhenAll(sends);
    }
}
