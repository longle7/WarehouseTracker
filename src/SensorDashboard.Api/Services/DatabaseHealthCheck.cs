using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SensorDashboard.Api.Services;

/// <summary>Healthy when the database answers and the schema is migrated (the queue table exists).</summary>
public sealed class DatabaseHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT OBJECT_ID(N'ingest.ReadingBatches');", connection);
            return await command.ExecuteScalarAsync(cancellationToken) is DBNull or null
                ? HealthCheckResult.Unhealthy("Database reachable but not migrated.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
