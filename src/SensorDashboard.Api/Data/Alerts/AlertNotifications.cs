using IoTDigitalTwin.Contracts.Alerts;

namespace SensorDashboard.Api.Data.Alerts;

public enum AlertEvent
{
    Opened,
    Escalated,
    Resolved,
}

public sealed record AlertNotification(AlertEvent Event, AlertDto Alert);

/// <summary>
/// Where alert notifications go. Logging only for now; an email, Teams or SMS sink (or an
/// SNS-style fan-out) plugs in here without touching the evaluator.
/// </summary>
public interface IAlertNotificationSink
{
    Task SendAsync(AlertNotification notification, CancellationToken cancellationToken);
}

public sealed class LoggingAlertNotificationSink(ILogger<LoggingAlertNotificationSink> logger) : IAlertNotificationSink
{
    public Task SendAsync(AlertNotification notification, CancellationToken cancellationToken)
    {
        var alert = notification.Alert;
        var level = notification.Event switch
        {
            AlertEvent.Escalated => LogLevel.Critical,
            AlertEvent.Opened when alert.Severity == AlertSeverity.Critical => LogLevel.Error,
            AlertEvent.Opened => LogLevel.Warning,
            _ => LogLevel.Information,
        };
        logger.Log(level, "Alert {Event}: {Severity} {Kind} on {SensorId} ({Location}, {WarehouseId}): {Message}",
            notification.Event, alert.Severity, alert.Kind, alert.SensorId, alert.Location, alert.WarehouseId, alert.Message);
        return Task.CompletedTask;
    }
}
