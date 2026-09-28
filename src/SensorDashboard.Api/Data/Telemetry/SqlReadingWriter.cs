using System.Data;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Data.SqlClient;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Sends each batch as a table-valued parameter in one round trip, bypassing EF change
/// tracking. The unique index on (SensorId, Timestamp) has IGNORE_DUP_KEY = ON, so retried
/// batches are absorbed by the database instead of raising key violations.
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

        SELECT @valid AS Valid, @@ROWCOUNT AS Inserted;
        """;

    public async Task<IngestResultDto> WriteAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(InsertSql, connection);
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
