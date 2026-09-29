using System.ComponentModel.DataAnnotations;

namespace SensorDashboard.Api.Monitoring;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>How often the health checks run and a sample is recorded for uptime history.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(30);
}
