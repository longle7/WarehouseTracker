using IoTDigitalTwin.Contracts.Alerts;
using Microsoft.EntityFrameworkCore;

namespace SensorDashboard.Api.Data.Alerts;

public enum AlertFilter
{
    /// <summary>Open or acknowledged.</summary>
    Active,
    Resolved,
    All,
}

public enum AcknowledgeOutcome
{
    Acknowledged,
    AlreadyAcknowledged,
    NotFound,
    AlreadyResolved,
}

/// <summary>Alert reads and operator actions (EF Core).</summary>
public sealed class AlertService(DigitalTwinDbContext db, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AlertDto>> ListAsync(
        AlertFilter filter, string? warehouseId, int limit, CancellationToken cancellationToken)
    {
        var query = db.Alerts.AsNoTracking().Include(a => a.Sensor).AsQueryable();
        query = filter switch
        {
            AlertFilter.Active => query.Where(a => a.ClosedAt == null),
            AlertFilter.Resolved => query.Where(a => a.ClosedAt != null),
            _ => query,
        };
        if (warehouseId is not null)
        {
            query = query.Where(a => a.WarehouseId == warehouseId);
        }

        // Active first, then most severe, then newest.
        var alerts = await query
            .OrderBy(a => a.ClosedAt != null)
            .ThenByDescending(a => a.Severity == AlertSeverity.Critical)
            .ThenByDescending(a => a.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return alerts.Select(a => ToDto(a, a.Sensor.Location)).ToList();
    }

    public async Task<(AcknowledgeOutcome Outcome, AlertDto? Alert)> AcknowledgeAsync(
        long id, string? by, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.Include(a => a.Sensor).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null) return (AcknowledgeOutcome.NotFound, null);
        if (alert.ClosedAt is not null) return (AcknowledgeOutcome.AlreadyResolved, ToDto(alert, alert.Sensor.Location));
        if (alert.AcknowledgedAt is not null) return (AcknowledgeOutcome.AlreadyAcknowledged, ToDto(alert, alert.Sensor.Location));

        alert.AcknowledgedAt = timeProvider.GetUtcNow();
        alert.AcknowledgedBy = string.IsNullOrWhiteSpace(by) ? "operator" : by.Trim()[..Math.Min(by.Trim().Length, 100)];
        await db.SaveChangesAsync(cancellationToken);
        return (AcknowledgeOutcome.Acknowledged, ToDto(alert, alert.Sensor.Location));
    }

    public static AlertDto ToDto(Alert a, string location) => new(
        a.Id,
        a.SensorId,
        a.WarehouseId,
        location,
        a.Kind,
        a.Severity,
        a.State,
        a.OpenedAt,
        a.LastSeenAt,
        a.ClosedAt,
        a.AcknowledgedAt,
        a.AcknowledgedBy,
        a.EscalatedAt,
        a.PeakTemperature,
        a.Message);
}
