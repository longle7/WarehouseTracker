using IoTDigitalTwin.Contracts.Alerts;
using SensorDashboard.Api.Data.Metadata;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Data.Alerts;

public abstract record AlertAction;

public sealed record OpenAlert(AlertKind Kind, AlertSeverity Severity, DateTimeOffset Since, double? Peak, string Message) : AlertAction;

public sealed record UpdateAlert(Alert Alert, AlertSeverity Severity, double? Peak) : AlertAction;

public sealed record CloseAlert(Alert Alert) : AlertAction;

public sealed record EscalateAlert(Alert Alert) : AlertAction;

/// <summary>
/// Pure alert lifecycle rules for one sensor: given what the readings say now and which
/// alerts are already active, decide what to open, update, close or escalate.
/// </summary>
public static class AlertRules
{
    /// <param name="watchingSince">
    /// When the evaluator started. A sensor that hasn't reported since then isn't known to be
    /// deployed and live, so going quiet doesn't open a new offline alert (this avoids a flood
    /// when the API starts before the producers). Already-active offline alerts continue.
    /// </param>
    public static IReadOnlyList<AlertAction> Evaluate(
        Sensor sensor,
        ConditionStreaks conditions,
        IReadOnlyCollection<Alert> activeAlerts,
        DateTimeOffset now,
        DateTimeOffset watchingSince,
        TimeSpan offlineAfter,
        AlertOptions options)
    {
        var actions = new List<AlertAction>();
        var active = activeAlerts.ToDictionary(a => a.Kind);

        // Disabled sensors (maintenance) don't alert; clear anything outstanding.
        if (!sensor.IsActive)
        {
            actions.AddRange(activeAlerts.Select(a => new CloseAlert(a)));
            return actions;
        }

        var lastReading = conditions.LastReadingAt;
        var offline = lastReading is null || now - lastReading > offlineAfter;
        var seenWhileWatching = lastReading >= watchingSince;
        Apply(AlertKind.SensorOffline,
            condition: offline && (seenWhileWatching || active.ContainsKey(AlertKind.SensorOffline)) ? lastReading ?? now : null,
            grace: TimeSpan.Zero, // offlineAfter already is the grace period
            // Offline resolves as soon as a fresh reading arrives.
            cleared: !offline,
            severity: AlertSeverity.Warning,
            peak: null,
            message: lastReading is null ? "No readings received" : $"No reading since {lastReading:u}");

        // Temperature and door alerts only resolve on evidence (a good reading); a sensor that
        // goes offline mid-excursion keeps its alert open alongside the offline one.
        var peak = PeakTemperature(sensor, conditions);
        var deviation = peak is { } p ? Math.Max(p - (double)sensor.MaxTemperatureF, (double)sensor.MinTemperatureF - p) : 0;
        Apply(AlertKind.TemperatureOutOfRange,
            condition: conditions.TemperatureOutOfRangeSince,
            grace: options.TemperatureGracePeriod,
            cleared: conditions.TemperatureOutOfRangeSince is null,
            severity: deviation >= options.CriticalDeviationF ? AlertSeverity.Critical : AlertSeverity.Warning,
            peak: peak,
            message: peak is { } t
                ? $"Temperature {t:0.0}°F outside safe range {sensor.MinTemperatureF:0.#}–{sensor.MaxTemperatureF:0.#}°F"
                : "Temperature outside safe range");

        Apply(AlertKind.DoorOpenTooLong,
            condition: conditions.DoorOpenSince,
            grace: options.DoorOpenGracePeriod,
            cleared: conditions.DoorOpenSince is null,
            severity: AlertSeverity.Warning,
            peak: null,
            message: "Door left open");

        return actions;

        void Apply(AlertKind kind, DateTimeOffset? condition, TimeSpan grace, bool cleared, AlertSeverity severity, double? peak, string message)
        {
            active.TryGetValue(kind, out var alert);

            if (alert is not null && cleared)
            {
                actions.Add(new CloseAlert(alert));
                return;
            }

            if (condition is not { } since || now - since < grace)
            {
                return;
            }

            if (alert is null)
            {
                actions.Add(new OpenAlert(kind, severity, since, peak, message));
                return;
            }

            // Severity only ratchets up while an alert is active.
            var newSeverity = severity > alert.Severity ? severity : alert.Severity;
            actions.Add(new UpdateAlert(alert, newSeverity, peak));

            if (newSeverity == AlertSeverity.Critical
                && alert.AcknowledgedAt is null
                && alert.EscalatedAt is null
                && now - alert.OpenedAt >= options.EscalateAfter)
            {
                actions.Add(new EscalateAlert(alert));
            }
        }
    }

    /// <summary>The reading during the current streak that is furthest from the safe range.</summary>
    private static double? PeakTemperature(Sensor sensor, ConditionStreaks conditions)
    {
        if (conditions.TemperatureOutOfRangeSince is null || conditions.StreakMax is not { } max || conditions.StreakMin is not { } min)
        {
            return null;
        }
        return max - (double)sensor.MaxTemperatureF >= (double)sensor.MinTemperatureF - min ? max : min;
    }
}
