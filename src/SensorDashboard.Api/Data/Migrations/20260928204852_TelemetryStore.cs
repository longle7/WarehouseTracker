using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Time-series store for readings, hand-written because EF Core can't model partitioning
    /// or columnstore. Not mapped in <see cref="DigitalTwinDbContext"/>: writes go through
    /// SqlReadingWriter, reads through dedicated queries.
    /// </summary>
    public partial class TelemetryStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA telemetry;");

            // Daily partitions on UTC timestamp. RANGE RIGHT: each boundary is the first
            // instant of its day. Boundaries are added by usp_MaintainReadingPartitions.
            migrationBuilder.Sql("CREATE PARTITION FUNCTION pf_ReadingsDaily (datetime2(3)) AS RANGE RIGHT FOR VALUES ();");
            migrationBuilder.Sql("CREATE PARTITION SCHEME ps_ReadingsDaily AS PARTITION pf_ReadingsDaily ALL TO ([PRIMARY]);");

            migrationBuilder.Sql("""
                CREATE TABLE telemetry.SensorReadings
                (
                    SensorId    varchar(32)   NOT NULL,
                    WarehouseId varchar(32)   NOT NULL,
                    [Timestamp] datetime2(3)  NOT NULL,
                    Temperature decimal(5,2)  NOT NULL,
                    Humidity    decimal(4,1)  NOT NULL,
                    DoorOpen    bit           NOT NULL,
                    IsAnomaly   bit           NOT NULL,
                    IngestedAt  datetime2(3)  NOT NULL CONSTRAINT DF_SensorReadings_IngestedAt DEFAULT SYSUTCDATETIME()
                ) ON ps_ReadingsDaily ([Timestamp]);
                """);

            // Columnstore for compression and fast range/aggregate scans.
            migrationBuilder.Sql("""
                CREATE CLUSTERED COLUMNSTORE INDEX CCI_SensorReadings
                    ON telemetry.SensorReadings
                    ON ps_ReadingsDaily ([Timestamp]);
                """);

            // Idempotent ingestion + per-sensor seeks. IGNORE_DUP_KEY drops duplicate rows
            // (e.g. from a retried batch) with a warning instead of failing the insert.
            migrationBuilder.Sql("""
                CREATE UNIQUE NONCLUSTERED INDEX UX_SensorReadings_SensorId_Timestamp
                    ON telemetry.SensorReadings (SensorId, [Timestamp])
                    WITH (IGNORE_DUP_KEY = ON)
                    ON ps_ReadingsDaily ([Timestamp]);
                """);

            migrationBuilder.Sql("""
                CREATE TYPE telemetry.SensorReadingTableType AS TABLE
                (
                    SensorId    varchar(32)   NOT NULL,
                    WarehouseId varchar(32)   NOT NULL,
                    [Timestamp] datetime2(3)  NOT NULL,
                    Temperature decimal(5,2)  NOT NULL,
                    Humidity    decimal(4,1)  NOT NULL,
                    DoorOpen    bit           NOT NULL,
                    IsAnomaly   bit           NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE telemetry.usp_MaintainReadingPartitions
                    @DaysAhead     int = 7,
                    @RetentionDays int = 30
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    DECLARE @today    datetime2(3) = CAST(CAST(SYSUTCDATETIME() AS date) AS datetime2(3));
                    DECLARE @cutoff   datetime2(3) = DATEADD(day, -@RetentionDays, @today);
                    DECLARE @day      datetime2(3) = @today;
                    DECLARE @split    int = 0;
                    DECLARE @dropped  int = 0;
                    DECLARE @lowest   datetime2(3);
                    DECLARE @second   datetime2(3);

                    -- 1. Create a boundary for today and each of the next @DaysAhead days. These
                    --    split the empty right-most partition, so they are metadata-only.
                    WHILE @day <= DATEADD(day, @DaysAhead, @today)
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM sys.partition_range_values rv
                            JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
                            WHERE pf.name = N'pf_ReadingsDaily' AND CAST(rv.value AS datetime2(3)) = @day)
                        BEGIN
                            ALTER PARTITION SCHEME ps_ReadingsDaily NEXT USED [PRIMARY];
                            ALTER PARTITION FUNCTION pf_ReadingsDaily() SPLIT RANGE (@day);
                            SET @split += 1;
                        END;

                        SET @day = DATEADD(day, 1, @day);
                    END;

                    -- 2. Retention. Partition 1 holds rows below the lowest boundary and partition 2
                    --    rows below the second. While both are entirely older than the cutoff,
                    --    truncate them and merge the lowest boundary; merging two empty
                    --    partitions moves no data.
                    WHILE 1 = 1
                    BEGIN
                        SELECT @lowest = MIN(v), @second = MAX(v)
                        FROM (
                            SELECT TOP (2) CAST(rv.value AS datetime2(3)) AS v
                            FROM sys.partition_range_values rv
                            JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
                            WHERE pf.name = N'pf_ReadingsDaily'
                            ORDER BY rv.boundary_id
                        ) b
                        HAVING COUNT(*) = 2;

                        IF @@ROWCOUNT = 0 OR @second > @cutoff BREAK;

                        TRUNCATE TABLE telemetry.SensorReadings WITH (PARTITIONS (1 TO 2));
                        ALTER PARTITION FUNCTION pf_ReadingsDaily() MERGE RANGE (@lowest);
                        SET @dropped += 1;
                    END;

                    SELECT @split AS PartitionsCreated,
                           @dropped AS PartitionsDropped,
                           (SELECT fanout FROM sys.partition_functions WHERE name = N'pf_ReadingsDaily') AS PartitionCount;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE telemetry.usp_MaintainReadingPartitions;");
            migrationBuilder.Sql("DROP TYPE telemetry.SensorReadingTableType;");
            migrationBuilder.Sql("DROP TABLE telemetry.SensorReadings;");
            migrationBuilder.Sql("DROP PARTITION SCHEME ps_ReadingsDaily;");
            migrationBuilder.Sql("DROP PARTITION FUNCTION pf_ReadingsDaily;");
            migrationBuilder.Sql("DROP SCHEMA telemetry;");
        }
    }
}
