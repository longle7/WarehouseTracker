using Dapper;
using IoTDigitalTwin.Contracts.Monitoring;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SensorDashboard.Api.Data;

namespace SensorDashboard.Api.Monitoring;

/// <summary>Builds GET /status: live health checks plus uptime and incidents from ops.HealthSamples.</summary>
public sealed class StatusService(
    HealthCheckService healthChecks,
    SqlConnectionFactory connections,
    TimeProvider timeProvider,
    ILogger<StatusService> logger)
{
    public async Task<SystemStatusDto> GetAsync(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var checks = report.Entries
            .Select(e => new HealthCheckResultDto(e.Key, Map(e.Value.Status), e.Value.Description, e.Value.Duration.TotalMilliseconds))
            .ToList();

        double? uptime24h = null, uptime7d = null;
        IReadOnlyList<IncidentDto> incidents = [];
        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken, retryOpen: false);
            uptime24h = await UptimeAsync(connection, now.AddHours(-24), cancellationToken);
            uptime7d = await UptimeAsync(connection, now.AddDays(-7), cancellationToken);
            incidents = await IncidentsAsync(connection, now.AddDays(-7), cancellationToken);
        }
        catch (Exception ex)
        {
            // The live checks above already show the database is the problem; keep answering.
            logger.LogDebug(ex, "Uptime history unavailable");
        }

        return new SystemStatusDto(Map(report.Status), now, uptime24h, uptime7d, checks, incidents);
    }

    private static Task<double?> UptimeAsync(System.Data.IDbConnection connection, DateTimeOffset since, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<double?>(new CommandDefinition(
            """
            SELECT 100.0 * SUM(CASE WHEN Status <> 'Unhealthy' THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0)
            FROM ops.HealthSamples WHERE CheckedAt >= @Since;
            """,
            new { Since = since.UtcDateTime }, cancellationToken: cancellationToken));

    // Gaps and islands: consecutive non-healthy samples form one incident; it ends at the
    // first healthy sample after its last bad one (or is still ongoing).
    private static async Task<IReadOnlyList<IncidentDto>> IncidentsAsync(
        System.Data.IDbConnection connection, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<IncidentRow>(new CommandDefinition(
            """
            WITH s AS (
                SELECT CheckedAt, Status, Failing,
                       CASE WHEN Status = 'Healthy' THEN 0 ELSE 1 END AS Bad,
                       ROW_NUMBER() OVER (ORDER BY CheckedAt)
                         - ROW_NUMBER() OVER (PARTITION BY CASE WHEN Status = 'Healthy' THEN 0 ELSE 1 END ORDER BY CheckedAt) AS Island
                FROM ops.HealthSamples WHERE CheckedAt >= @Since),
            islands AS (
                SELECT Island, MIN(CheckedAt) AS StartedAt, MAX(CheckedAt) AS LastBadAt,
                       MAX(CASE WHEN Status = 'Unhealthy' THEN 2 ELSE 1 END) AS Severity,
                       MIN(Failing) AS Summary
                FROM s WHERE Bad = 1 GROUP BY Island)
            SELECT TOP (10) i.StartedAt,
                   (SELECT MIN(h.CheckedAt) FROM ops.HealthSamples h WHERE h.CheckedAt > i.LastBadAt AND h.Status = 'Healthy') AS EndedAt,
                   i.Severity, i.Summary
            FROM islands i
            ORDER BY i.StartedAt DESC;
            """,
            new { Since = since.UtcDateTime }, cancellationToken: cancellationToken));

        return rows.Select(r => new IncidentDto(
            r.StartedAt.AsUtc(),
            r.EndedAt?.AsUtc(),
            r.Severity == 2 ? HealthState.Unhealthy : HealthState.Degraded,
            string.IsNullOrEmpty(r.Summary) ? "Unknown" : r.Summary)).ToList();
    }

    private static HealthState Map(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => HealthState.Healthy,
        HealthStatus.Degraded => HealthState.Degraded,
        _ => HealthState.Unhealthy,
    };

    private sealed record IncidentRow(DateTime StartedAt, DateTime? EndedAt, int Severity, string? Summary);
}
