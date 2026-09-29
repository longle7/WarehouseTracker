using IoTDigitalTwin.Contracts.Alerts;
using IoTDigitalTwin.Contracts.Metadata;
using IoTDigitalTwin.Contracts.Telemetry;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Services;

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
/// Subscriptions are idempotent, validated and capped per connection.
/// </summary>
public sealed partial class TelemetryHub(
    LiveConnectionTracker tracker,
    SubscriptionRegistry subscriptions,
    IOptions<ApiLimitsOptions> limits) : Hub<ITelemetryClient>
{
    public static string WarehouseGroup(string warehouseId) => $"warehouse:{warehouseId}";

    public static string SensorGroup(string sensorId) => $"sensor:{sensorId}";

    public Task<SubscribeOutcome> SubscribeWarehouse(string warehouseId) => SubscribeAsync(WarehouseGroup(Validate(warehouseId)));

    public Task UnsubscribeWarehouse(string warehouseId) => UnsubscribeAsync(WarehouseGroup(Validate(warehouseId)));

    public Task<SubscribeOutcome> SubscribeSensor(string sensorId) => SubscribeAsync(SensorGroup(Validate(sensorId)));

    public Task UnsubscribeSensor(string sensorId) => UnsubscribeAsync(SensorGroup(Validate(sensorId)));

    public override Task OnConnectedAsync()
    {
        tracker.Connected();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Disconnected();
        subscriptions.RemoveConnection(Context.ConnectionId); // SignalR drops the group memberships itself
        return base.OnDisconnectedAsync(exception);
    }

    private async Task<SubscribeOutcome> SubscribeAsync(string group)
    {
        var outcome = subscriptions.TryAdd(Context.ConnectionId, group, limits.Value.MaxSubscriptionsPerConnection);
        switch (outcome)
        {
            case SubscribeOutcome.LimitReached:
                throw new HubException($"Subscription limit of {limits.Value.MaxSubscriptionsPerConnection} reached; unsubscribe first.");
            case SubscribeOutcome.Added:
                await Groups.AddToGroupAsync(Context.ConnectionId, group);
                break;
        }
        return outcome;
    }

    private async Task UnsubscribeAsync(string group)
    {
        if (subscriptions.Remove(Context.ConnectionId, group))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
    }

    // IDs are short slugs like WH-SEA or WH-SEA-FRG-01; anything else can't match a group.
    private static string Validate(string id) =>
        id is not null && IdPattern().IsMatch(id) ? id : throw new HubException("Invalid warehouse or sensor ID.");

    [GeneratedRegex("^[A-Za-z0-9-]{1,32}$")]
    private static partial Regex IdPattern();
}

/// <summary>Lets the broadcaster skip building snapshots when no dashboard is connected.</summary>
public sealed class LiveConnectionTracker
{
    private int _count;

    public bool HasConnections => Volatile.Read(ref _count) > 0;

    public void Connected() => Interlocked.Increment(ref _count);

    public void Disconnected() => Interlocked.Decrement(ref _count);
}
