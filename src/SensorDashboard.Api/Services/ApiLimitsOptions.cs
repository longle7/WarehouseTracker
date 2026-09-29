using System.ComponentModel.DataAnnotations;

namespace SensorDashboard.Api.Services;

/// <summary>Server-wide limits that keep one client or one slow dependency from taking the API down.</summary>
public sealed class ApiLimitsOptions
{
    public const string SectionName = "ApiLimits";

    /// <summary>Sustained requests per second allowed per client across all endpoints (token bucket refill).</summary>
    [Range(1, 100_000)]
    public int RequestsPerSecond { get; set; } = 100;

    /// <summary>Burst a client may make above the sustained rate.</summary>
    [Range(1, 100_000)]
    public int Burst { get; set; } = 200;

    /// <summary>Longest a request may run before the API gives up with 504.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Default SQL command timeout for request-path queries (maintenance jobs use their own).</summary>
    [Range(1, 300)]
    public int SqlCommandTimeoutSeconds { get; set; } = 15;

    /// <summary>Hub groups (warehouse/sensor subscriptions) one SignalR connection may hold.</summary>
    [Range(1, 1000)]
    public int MaxSubscriptionsPerConnection { get; set; } = 20;
}
