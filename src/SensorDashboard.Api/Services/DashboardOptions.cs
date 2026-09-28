namespace SensorDashboard.Api.Services;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// A sensor with no reading for this long is offline. Default is 4 missed ticks at the
    /// simulator's 5s interval, short enough to surface its simulated outages.
    /// </summary>
    public TimeSpan OfflineAfter { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How far back to look for a sensor's last reading (shown even when offline).</summary>
    public TimeSpan LastReadingLookback { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Default history window when a request gives no <c>from</c>.</summary>
    public TimeSpan DefaultHistoryWindow { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan MaxHistoryWindow { get; set; } = TimeSpan.FromDays(31);

    /// <summary>
    /// Live snapshots are pushed after each ingest and at least this often, so sensors that
    /// stop reporting flip to offline on connected dashboards without any new readings.
    /// </summary>
    public TimeSpan LiveHeartbeat { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Target point count when choosing a bucket size automatically.</summary>
    public int TargetHistoryPoints { get; set; } = 300;
}
