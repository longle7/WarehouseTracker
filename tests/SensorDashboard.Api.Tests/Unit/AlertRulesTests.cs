using IoTDigitalTwin.Contracts.Alerts;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Data.Metadata;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Tests.Unit;

public class AlertRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(20);
    // The evaluator started watching an hour ago unless a test says otherwise.
    private static readonly DateTimeOffset WatchingSince = Now.AddHours(-1);
    private static readonly AlertOptions Options = new()
    {
        TemperatureGracePeriod = TimeSpan.FromSeconds(30),
        DoorOpenGracePeriod = TimeSpan.FromSeconds(30),
        CriticalDeviationF = 5,
        EscalateAfter = TimeSpan.FromMinutes(5),
    };

    private static Sensor Sensor(bool active = true) => new()
    {
        Id = "S1", WarehouseId = "W1", Location = "Test", MinTemperatureF = 34, MaxTemperatureF = 40, IsActive = active,
    };

    /// <summary>A sensor that reported a second ago, with optional streaks that began N seconds ago.</summary>
    private static ConditionStreaks Streaks(double? tempSecondsAgo = null, double min = 41, double max = 42, double? doorSecondsAgo = null) =>
        new(
            Now.AddSeconds(-1),
            tempSecondsAgo is { } t ? Now.AddSeconds(-t) : null,
            tempSecondsAgo is null ? null : min,
            tempSecondsAgo is null ? null : max,
            doorSecondsAgo is { } d ? Now.AddSeconds(-d) : null);

    private static Alert Active(AlertKind kind, AlertSeverity severity = AlertSeverity.Warning, double openedSecondsAgo = 60, bool acknowledged = false) => new()
    {
        Id = 1, SensorId = "S1", WarehouseId = "W1", Kind = kind, Severity = severity, Message = "",
        OpenedAt = Now.AddSeconds(-openedSecondsAgo), LastSeenAt = Now.AddSeconds(-5),
        AcknowledgedAt = acknowledged ? Now.AddSeconds(-10) : null,
    };

    private static IReadOnlyList<AlertAction> Evaluate(ConditionStreaks streaks, params Alert[] active) =>
        AlertRules.Evaluate(Sensor(), streaks, active, Now, WatchingSince, OfflineAfter, Options);

    [Fact]
    public void Healthy_sensor_raises_nothing() => Assert.Empty(Evaluate(Streaks()));

    [Fact]
    public void Brief_excursion_within_the_grace_period_raises_nothing() => Assert.Empty(Evaluate(Streaks(tempSecondsAgo: 20)));

    [Fact]
    public void Sustained_excursion_opens_a_temperature_alert_dated_from_the_streak_start()
    {
        var open = Assert.IsType<OpenAlert>(Assert.Single(Evaluate(Streaks(tempSecondsAgo: 45, max: 42))));

        Assert.Equal(AlertKind.TemperatureOutOfRange, open.Kind);
        Assert.Equal(AlertSeverity.Warning, open.Severity); // 2°F over
        Assert.Equal(Now.AddSeconds(-45), open.Since);
        Assert.Equal(42, open.Peak);
    }

    [Theory]
    [InlineData(34, 46, 46)] // 6°F over
    [InlineData(28, 36, 28)] // 6°F under
    public void Large_deviation_is_critical_and_peak_is_the_furthest_reading(double min, double max, double expectedPeak)
    {
        var open = Assert.IsType<OpenAlert>(Assert.Single(Evaluate(Streaks(tempSecondsAgo: 45, min: min, max: max))));

        Assert.Equal(AlertSeverity.Critical, open.Severity);
        Assert.Equal(expectedPeak, open.Peak);
    }

    [Fact]
    public void Ongoing_condition_updates_the_alert_and_severity_only_ratchets_up()
    {
        var alert = Active(AlertKind.TemperatureOutOfRange, AlertSeverity.Critical);

        var update = Assert.IsType<UpdateAlert>(Assert.Single(Evaluate(Streaks(tempSecondsAgo: 60, max: 41), alert)));

        Assert.Equal(AlertSeverity.Critical, update.Severity);
    }

    [Fact]
    public void Good_reading_resolves_the_alert()
    {
        var alert = Active(AlertKind.TemperatureOutOfRange);
        Assert.IsType<CloseAlert>(Assert.Single(Evaluate(Streaks(), alert)));
    }

    [Fact]
    public void Going_offline_keeps_the_temperature_alert_open_and_adds_an_offline_alert()
    {
        var temperature = Active(AlertKind.TemperatureOutOfRange);
        var stale = new ConditionStreaks(Now.AddSeconds(-60), Now.AddSeconds(-120), 41, 45, null); // quiet for 60 s, watched for an hour

        var actions = Evaluate(stale, temperature);

        Assert.Contains(actions, a => a is OpenAlert { Kind: AlertKind.SensorOffline });
        Assert.Contains(actions, a => a is UpdateAlert u && u.Alert == temperature);
        Assert.DoesNotContain(actions, a => a is CloseAlert);
    }

    [Fact]
    public void Sensor_that_goes_quiet_while_watched_opens_offline_and_a_fresh_reading_resolves_it()
    {
        var wentQuiet = new ConditionStreaks(Now.AddSeconds(-30), null, null, null, null);
        Assert.Contains(Evaluate(wentQuiet), a => a is OpenAlert { Kind: AlertKind.SensorOffline });

        var offline = Active(AlertKind.SensorOffline);
        Assert.IsType<CloseAlert>(Assert.Single(Evaluate(Streaks(), offline)));
    }

    [Fact]
    public void Sensors_not_heard_from_since_startup_do_not_open_offline_alerts()
    {
        // API just started (watching for 5 s); the producers haven't connected yet.
        var justStarted = Now.AddSeconds(-5);
        var lastHeardBeforeRestart = new ConditionStreaks(Now.AddMinutes(-10), null, null, null, null);

        Assert.Empty(AlertRules.Evaluate(Sensor(), ConditionStreaks.None, [], Now, justStarted, OfflineAfter, Options));
        Assert.Empty(AlertRules.Evaluate(Sensor(), lastHeardBeforeRestart, [], Now, justStarted, OfflineAfter, Options));
    }

    [Fact]
    public void Offline_alert_opened_before_a_restart_stays_active()
    {
        var justStarted = Now.AddSeconds(-5);
        var offline = Active(AlertKind.SensorOffline, openedSecondsAgo: 600);
        var silent = new ConditionStreaks(Now.AddMinutes(-10), null, null, null, null);

        var action = Assert.Single(AlertRules.Evaluate(Sensor(), silent, [offline], Now, justStarted, OfflineAfter, Options));
        Assert.IsType<UpdateAlert>(action);
    }

    [Fact]
    public void Door_open_past_the_grace_period_opens_a_door_alert() =>
        Assert.Contains(Evaluate(Streaks(doorSecondsAgo: 40)), a => a is OpenAlert { Kind: AlertKind.DoorOpenTooLong });

    [Fact]
    public void Unacknowledged_critical_alert_escalates_once_after_the_threshold()
    {
        var alert = Active(AlertKind.TemperatureOutOfRange, AlertSeverity.Critical, openedSecondsAgo: 301);
        Assert.Contains(Evaluate(Streaks(tempSecondsAgo: 301, max: 46), alert), a => a is EscalateAlert);

        alert.EscalatedAt = Now.AddSeconds(-5);
        Assert.DoesNotContain(Evaluate(Streaks(tempSecondsAgo: 301, max: 46), alert), a => a is EscalateAlert);
    }

    [Fact]
    public void Acknowledged_alerts_do_not_escalate()
    {
        var alert = Active(AlertKind.TemperatureOutOfRange, AlertSeverity.Critical, openedSecondsAgo: 600, acknowledged: true);
        Assert.DoesNotContain(Evaluate(Streaks(tempSecondsAgo: 600, max: 46), alert), a => a is EscalateAlert);
    }

    [Fact]
    public void Inactive_sensor_closes_everything_and_raises_nothing()
    {
        var alerts = new[] { Active(AlertKind.TemperatureOutOfRange), Active(AlertKind.SensorOffline) };

        var actions = AlertRules.Evaluate(Sensor(active: false), ConditionStreaks.None, alerts, Now, WatchingSince, OfflineAfter, Options);

        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.IsType<CloseAlert>(a));
    }
}
