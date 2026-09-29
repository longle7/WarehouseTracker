using System.ComponentModel.DataAnnotations;

namespace SensorDashboard.Api.Data.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a temperature must stay out of range before an alert opens. Filters out brief
    /// excursions such as a door opened for a delivery.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan TemperatureGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan DoorOpenGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A temperature alert is critical once any reading is this far outside the range.</summary>
    [Range(0.1, 100)]
    public double CriticalDeviationF { get; set; } = 5;

    /// <summary>Critical alerts still unacknowledged after this long notify again, flagged as escalated.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan EscalateAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How far back to look for the start of a condition.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Lookback { get; set; } = TimeSpan.FromHours(1);
}
