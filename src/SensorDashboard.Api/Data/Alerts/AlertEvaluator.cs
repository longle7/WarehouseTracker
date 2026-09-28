using IoTDigitalTwin.Contracts.Alerts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Data.Alerts;

/// <summary>
/// Runs the alert lifecycle on a timer: reads condition streaks from the time-series store,
/// applies <see cref="AlertRules"/> per sensor, persists the changes, then notifies the sink
/// and pushes <c>AlertsChanged</c> to dashboards. Works from stored readings, not in-memory
/// state, so it picks up where it left off after a restart.
/// </summary>
public sealed class AlertEvaluator(
    IServiceScopeFactory scopeFactory,
    IReadingQueries readings,
    IAlertNotificationSink notifications,
    IHubContext<TelemetryHub, ITelemetryClient> hub,
    IOptions<AlertOptions> alertOptions,
    IOptions<DashboardOptions> dashboardOptions,
    TimeProvider timeProvider,
    ILogger<AlertEvaluator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(alertOptions.Value.EvaluationInterval, timeProvider);
        do
        {
            try
            {
                await EvaluateOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Alert evaluation failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task EvaluateOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DigitalTwinDbContext>();
        var options = alertOptions.Value;
        var now = timeProvider.GetUtcNow();

        var sensors = await db.Sensors.AsNoTracking().ToListAsync(cancellationToken);
        var active = await db.Alerts.Where(a => a.ClosedAt == null).ToListAsync(cancellationToken);
        var streaks = await readings.GetConditionStreaksAsync(
            sensors.Select(s => new SensorThresholds(s.Id, s.MinTemperatureF, s.MaxTemperatureF)).ToList(),
            now - options.Lookback,
            cancellationToken);

        var changes = new List<(AlertEvent Event, Alert Alert)>();
        foreach (var sensor in sensors)
        {
            var actions = AlertRules.Evaluate(
                sensor,
                streaks.GetValueOrDefault(sensor.Id, ConditionStreaks.None),
                active.Where(a => a.SensorId == sensor.Id).ToList(),
                now,
                dashboardOptions.Value.OfflineAfter,
                options);

            foreach (var action in actions)
            {
                switch (action)
                {
                    case OpenAlert open:
                        var alert = new Alert
                        {
                            SensorId = sensor.Id,
                            WarehouseId = sensor.WarehouseId,
                            Kind = open.Kind,
                            Severity = open.Severity,
                            OpenedAt = open.Since,
                            LastSeenAt = now,
                            PeakTemperature = open.Peak,
                            Message = open.Message,
                        };
                        db.Alerts.Add(alert);
                        changes.Add((AlertEvent.Opened, alert));
                        break;
                    case UpdateAlert update:
                        update.Alert.LastSeenAt = now;
                        update.Alert.Severity = update.Severity;
                        if (update.Peak is not null) update.Alert.PeakTemperature = update.Peak;
                        break;
                    case CloseAlert close:
                        close.Alert.ClosedAt = now;
                        changes.Add((AlertEvent.Resolved, close.Alert));
                        break;
                    case EscalateAlert escalate:
                        escalate.Alert.EscalatedAt = now;
                        changes.Add((AlertEvent.Escalated, escalate.Alert));
                        break;
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another instance opened the same alert first (UX_Alerts_Active); it will notify.
            logger.LogDebug("Alert already opened by another evaluator; skipping this round");
            return;
        }

        if (changes.Count == 0)
        {
            return;
        }

        var locations = sensors.ToDictionary(s => s.Id, s => s.Location);
        var dtos = changes.Select(c => (c.Event, Dto: AlertService.ToDto(c.Alert, locations[c.Alert.SensorId]))).ToList();
        foreach (var (evt, dto) in dtos)
        {
            await notifications.SendAsync(new AlertNotification(evt, dto), cancellationToken);
        }
        await hub.Clients.All.AlertsChanged(dtos.Select(d => d.Dto).ToList());
    }
}
