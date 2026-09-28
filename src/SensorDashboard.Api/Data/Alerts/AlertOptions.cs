namespace SensorDashboard.Api.Data.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a temperature must stay out of range before an alert opens. Filters out brief
    /// excursions such as a door opened for a delivery.
    /// </summary>
    public TimeSpan TemperatureGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan DoorOpenGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A temperature alert is critical once any reading is this far outside the range.</summary>
    public double CriticalDeviationF { get; set; } = 5;

    /// <summary>Critical alerts still unacknowledged after this long notify again, flagged as escalated.</summary>
    public TimeSpan EscalateAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How far back to look for the start of a condition.</summary>
    public TimeSpan Lookback { get; set; } = TimeSpan.FromHours(1);
}
