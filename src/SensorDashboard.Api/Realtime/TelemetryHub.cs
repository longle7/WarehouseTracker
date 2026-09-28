using IoTDigitalTwin.Contracts.Alerts;
using IoTDigitalTwin.Contracts.Metadata;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.SignalR;

namespace SensorDashboard.Api.Realtime;

/// <summary>
/// Messages the server pushes to dashboards. Method names are the event names clients
/// subscribe to (matched case-insensitively by the JavaScript client).
/// </summary>
public interface ITelemetryClient
{
    /// <summary>Sent to every connection.</summary>
    Task WarehousesUpdated(IReadOnlyList<WarehouseDto> warehouses);

    /// <summary>Sent to connections subscribed to the warehouse.</summary>
    Task SensorsUpdated(string warehouseId, IReadOnlyList<SensorDto> sensors);

    /// <summary>Sent to connections subscribed to the sensor, with its newly ingested readings.</summary>
    Task ReadingsIngested(string sensorId, IReadOnlyList<SensorReadingDto> readings);

    /// <summary>Sent to every connection when alerts open, escalate, resolve or are acknowledged.</summary>
    Task AlertsChanged(IReadOnlyList<AlertDto> alerts);
}

/// <summary>
/// Live dashboard updates (the API Gateway WebSocket route of the AWS design). Clients join
/// groups for the warehouse and sensor they are viewing; the overview goes to everyone.
/// </summary>
public sealed class TelemetryHub(LiveConnectionTracker tracker) : Hub<ITelemetryClient>
{
    public static string WarehouseGroup(string warehouseId) => $"warehouse:{warehouseId}";

    public static string SensorGroup(string sensorId) => $"sensor:{sensorId}";

    public Task SubscribeWarehouse(string warehouseId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, WarehouseGroup(warehouseId));

    public Task UnsubscribeWarehouse(string warehouseId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, WarehouseGroup(warehouseId));

    public Task SubscribeSensor(string sensorId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, SensorGroup(sensorId));

    public Task UnsubscribeSensor(string sensorId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SensorGroup(sensorId));

    public override Task OnConnectedAsync()
    {
        tracker.Connected();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Disconnected();
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Lets the broadcaster skip building snapshots when no dashboard is connected.</summary>
public sealed class LiveConnectionTracker
{
    private int _count;

    public bool HasConnections => Volatile.Read(ref _count) > 0;

    public void Connected() => Interlocked.Increment(ref _count);

    public void Disconnected() => Interlocked.Decrement(ref _count);
}
