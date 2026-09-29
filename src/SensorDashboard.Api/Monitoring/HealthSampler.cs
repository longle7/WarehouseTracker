using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SensorDashboard.Api.Data;

namespace SensorDashboard.Api.Monitoring;

/// <summary>
/// Runs the health checks on a schedule (ASP.NET Core's health check publisher), records each
/// result in ops.HealthSamples for uptime history, and logs every change of state: a warning
/// when degraded, an error when unhealthy, info on recovery. If the database itself is down,
/// samples wait in a bounded buffer and are written once it's back, so outages still show up.
/// </summary>
public sealed class HealthSampler(SqlConnectionFactory connections, ILogger<HealthSampler> logger) : IHealthCheckPublisher
{
    private const int MaxBuffered = 2000; // ~16 hours at the 30 s default
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly ConcurrentQueue<Sample> _pending = new();
    private HealthStatus? _lastStatus;
    private DateTime _nextTrim = DateTime.UtcNow;

    public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        LogTransition(report);

        var failing = report.Entries
            .Where(e => e.Value.Status != HealthStatus.Healthy)
            .Select(e => $"{e.Key}: {e.Value.Description ?? e.Value.Status.ToString()}");
        var summary = string.Join("; ", failing);
        _pending.Enqueue(new Sample(
            DateTime.UtcNow, report.Status.ToString(), (int)report.TotalDuration.TotalMilliseconds, summary.Length > 1000 ? summary[..1000] : summary));
        while (_pending.Count > MaxBuffered) _pending.TryDequeue(out _);

        try
        {
            await FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Expected while the database is down; the samples stay buffered for next time.
            logger.LogDebug(ex, "Couldn't record health samples ({Pending} buffered)", _pending.Count);
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.IsEmpty) return;
        await using var connection = await connections.OpenAsync(cancellationToken, retryOpen: false);
        while (_pending.TryPeek(out var sample))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT ops.HealthSamples (CheckedAt, Status, DurationMs, Failing) VALUES (@CheckedAt, @Status, @DurationMs, @Failing);",
                sample, cancellationToken: cancellationToken));
            _pending.TryDequeue(out _);
        }

        if (DateTime.UtcNow >= _nextTrim)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE TOP (10000) FROM ops.HealthSamples WHERE CheckedAt < @Cutoff;",
                new { Cutoff = DateTime.UtcNow - Retention }, cancellationToken: cancellationToken));
            _nextTrim = DateTime.UtcNow.AddHours(1);
        }
    }

    private void LogTransition(HealthReport report)
    {
        if (report.Status == _lastStatus) return;
        var previous = _lastStatus;
        _lastStatus = report.Status;
        if (previous is null && report.Status == HealthStatus.Healthy) return; // quiet, healthy start

        var detail = string.Join("; ", report.Entries
            .Where(e => e.Value.Status != HealthStatus.Healthy)
            .Select(e => $"{e.Key}: {e.Value.Description}"));
        switch (report.Status)
        {
            case HealthStatus.Unhealthy:
                logger.LogError(report.Entries.Values.FirstOrDefault(e => e.Exception is not null).Exception,
                    "Health changed {Previous} -> Unhealthy: {Detail}", previous?.ToString() ?? "unknown", detail);
                break;
            case HealthStatus.Degraded:
                logger.LogWarning("Health changed {Previous} -> Degraded: {Detail}", previous?.ToString() ?? "unknown", detail);
                break;
            default:
                logger.LogInformation("Health recovered {Previous} -> Healthy", previous?.ToString() ?? "unknown");
                break;
        }
    }

    private sealed record Sample(DateTime CheckedAt, string Status, int DurationMs, string Failing);
}
