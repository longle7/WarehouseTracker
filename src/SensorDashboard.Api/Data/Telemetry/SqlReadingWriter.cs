using System.Data;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Data.SqlClient;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Sends each batch as a table-valued parameter in one round trip, bypassing EF change
/// tracking. The unique index on (SensorId, Timestamp) has IGNORE_DUP_KEY = ON, so retried
/// batches are absorbed by the database instead of raising key violations. Touched minutes
/// are marked dirty for the 1-minute rollups in the same batch.
/// </summary>
public sealed class SqlReadingWriter(string connectionString) : IReadingWriter
{
    private const string InsertSql = """
        SET NOCOUNT ON;

        DECLARE @valid int =
            (SELECT COUNT(*)
             FROM @Readings r
             JOIN metadata.Sensors s ON s.Id = r.SensorId AND s.WarehouseId = r.WarehouseId AND s.IsActive = 1);

        INSERT telemetry.SensorReadings (SensorId, WarehouseId, [Timestamp], Temperature, Humidity, DoorOpen, IsAnomaly)
        SELECT r.SensorId, r.WarehouseId, r.[Timestamp], r.Temperature, r.Humidity, r.DoorOpen, r.IsAnomaly
        FROM @Readings r
        JOIN metadata.Sensors s ON s.Id = r.SensorId AND s.WarehouseId = r.WarehouseId AND s.IsActive = 1;

        DECLARE @inserted int = @@ROWCOUNT;

        -- Mark touched minutes for RollupService (same batch, so no write goes unrolled).
        IF @inserted > 0
            INSERT telemetry.RollupDirty (SensorId, BucketStart)
            SELECT DISTINCT r.SensorId, DATE_BUCKET(minute, 1, r.[Timestamp])
            FROM @Readings r
            JOIN metadata.Sensors s ON s.Id = r.SensorId AND s.WarehouseId = r.WarehouseId AND s.IsActive = 1;

        SELECT @valid AS Valid, @inserted AS Inserted;
        """;

    public async Task<IngestResultDto> WriteAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await WriteAsync(connection, null, readings, cancellationToken);
    }

    /// <summary>
    /// Writes on the caller's connection and transaction, so the queue consumer can store a
    /// batch and remove it from the queue atomically.
    /// </summary>
    public static async Task<IngestResultDto> WriteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<SensorReadingDto> readings,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(InsertSql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@Readings", SqlDbType.Structured)
        {
            TypeName = "telemetry.SensorReadingTableType",
            Value = ToTable(readings),
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var valid = reader.GetInt32(0);
        var inserted = reader.GetInt32(1);

        return new IngestResultDto(
            Received: readings.Count,
            Inserted: inserted,
            Duplicates: valid - inserted,
            Rejected: readings.Count - valid);
    }

    private static DataTable ToTable(IReadOnlyList<SensorReadingDto> readings)
    {
        // Column order must match telemetry.SensorReadingTableType.
        var table = new DataTable();
        table.Columns.Add("SensorId", typeof(string));
        table.Columns.Add("WarehouseId", typeof(string));
        table.Columns.Add("Timestamp", typeof(DateTime));
        table.Columns.Add("Temperature", typeof(decimal));
        table.Columns.Add("Humidity", typeof(decimal));
        table.Columns.Add("DoorOpen", typeof(bool));
        table.Columns.Add("IsAnomaly", typeof(bool));

        foreach (var r in readings)
        {
            table.Rows.Add(
                r.SensorId,
                r.WarehouseId,
                r.Timestamp.UtcDateTime,
                Math.Round((decimal)r.Temperature, 2),
                Math.Round((decimal)r.Humidity, 1),
                r.DoorOpen,
                r.IsAnomaly);
        }

        return table;
    }
}
