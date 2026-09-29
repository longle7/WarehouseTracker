using Microsoft.Extensions.Diagnostics.HealthChecks;
using SensorDashboard.Api.Data.Ingestion;

namespace SensorDashboard.Api.Monitoring;

/// <summary>Degraded when the ingest backlog is getting old, unhealthy when it's badly behind.</summary>
public sealed class IngestQueueHealthCheck(SqlReadingQueue queue) : IHealthCheck
{
    private static readonly TimeSpan Lagging = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Stalled = TimeSpan.FromMinutes(10);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        int depth, deadLetters;
        double? oldestAgeSeconds;
        try
        {
            (depth, oldestAgeSeconds, deadLetters) = await queue.GetStatsAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the reason short for incident history; the exception is still attached for logs.
            return HealthCheckResult.Unhealthy("Queue unavailable (database unreachable).", ex);
        }
        var age = TimeSpan.FromSeconds(oldestAgeSeconds ?? 0);
        var summary = $"{depth} queued, oldest {age.TotalSeconds:0}s, {deadLetters} dead-lettered";
        var data = new Dictionary<string, object> { ["depth"] = depth, ["oldestAgeSeconds"] = age.TotalSeconds, ["deadLetters"] = deadLetters };

        if (age >= Stalled) return HealthCheckResult.Unhealthy($"Ingestion stalled: {summary}", data: data);
        if (age >= Lagging) return HealthCheckResult.Degraded($"Ingestion lagging: {summary}", data: data);
        return HealthCheckResult.Healthy(summary, data);
    }
}

/// <summary>
/// Degraded when no readings have been stored recently: the API is fine, but its producers
/// (sensors, simulator) have gone quiet, which an operator should know about.
/// </summary>
public sealed class DataFreshnessHealthCheck(IngestMetrics metrics, TimeProvider timeProvider) : IHealthCheck
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var last = metrics.LastReadingsStoredAt;
        return Task.FromResult(last switch
        {
            null => HealthCheckResult.Degraded("No readings stored since the API started."),
            { } at when timeProvider.GetUtcNow() - at > Stale =>
                HealthCheckResult.Degraded($"No readings stored for {(timeProvider.GetUtcNow() - at).TotalMinutes:0} minutes."),
            { } at => HealthCheckResult.Healthy($"Last readings stored {(timeProvider.GetUtcNow() - at).TotalSeconds:0}s ago."),
        });
    }
}
