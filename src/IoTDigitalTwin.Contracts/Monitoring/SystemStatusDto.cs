namespace IoTDigitalTwin.Contracts.Monitoring;

/// <summary>GET /status: a live health check plus uptime history from the health sampler.</summary>
/// <param name="Uptime24h">Share of samples in the last 24 h that were up (healthy or degraded), 0-100; null before any samples.</param>
public sealed record SystemStatusDto(
    HealthState Status,
    DateTimeOffset CheckedAt,
    double? Uptime24h,
    double? Uptime7d,
    IReadOnlyList<HealthCheckResultDto> Checks,
    IReadOnlyList<IncidentDto> Incidents);

public sealed record HealthCheckResultDto(string Name, HealthState Status, string? Description, double DurationMs);

/// <summary>A continuous run of non-healthy samples.</summary>
/// <param name="EndedAt">First healthy sample afterwards; null while ongoing.</param>
/// <param name="Summary">The failing checks when it started.</param>
public sealed record IncidentDto(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, HealthState Status, string Summary);

/// <summary>Body of POST /client-errors: an unexpected error in the dashboard, for the server log.</summary>
public sealed record ClientErrorReportDto(string Message, string? Stack, string Source, string Url);

public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
}
