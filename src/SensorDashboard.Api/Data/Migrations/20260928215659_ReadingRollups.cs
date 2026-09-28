using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// 1-minute rollups of readings (the equivalent of TimescaleDB continuous aggregates),
    /// refreshed incrementally from a dirty-bucket list the writer maintains.
    /// </summary>
    public partial class ReadingRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sums and counts rather than averages, so minutes re-aggregate exactly into any
            // larger bucket. Rows outlive raw-reading retention.
            migrationBuilder.Sql("""
                CREATE TABLE telemetry.SensorReadings1m
                (
                    SensorId       varchar(32)    NOT NULL,
                    BucketStart    datetime2(3)   NOT NULL,
                    ReadingCount   int            NOT NULL,
                    SumTemperature decimal(12,2)  NOT NULL,
                    MinTemperature decimal(5,2)   NOT NULL,
                    MaxTemperature decimal(5,2)   NOT NULL,
                    SumHumidity    decimal(12,1)  NOT NULL,
                    MinHumidity    decimal(4,1)   NOT NULL,
                    MaxHumidity    decimal(4,1)   NOT NULL,
                    DoorOpenCount  int            NOT NULL,
                    AnomalyCount   int            NOT NULL,
                    RefreshedAt    datetime2(3)   NOT NULL CONSTRAINT DF_SensorReadings1m_RefreshedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_SensorReadings1m PRIMARY KEY (SensorId, BucketStart)
                );
                """);

            // Minutes whose rollup is stale. The writer marks them in the same batch as the
            // insert; IGNORE_DUP_KEY makes re-marking an already-dirty minute a no-op.
            migrationBuilder.Sql("""
                CREATE TABLE telemetry.RollupDirty
                (
                    SensorId    varchar(32)  NOT NULL,
                    BucketStart datetime2(3) NOT NULL,
                    CONSTRAINT PK_RollupDirty PRIMARY KEY (SensorId, BucketStart) WITH (IGNORE_DUP_KEY = ON)
                );
                """);

            // Recompute a batch of dirty minutes from raw readings. Reading raw under the
            // default locking READ COMMITTED waits for any in-flight insert into the same
            // minute, so a concurrent write is never missed.
            migrationBuilder.Sql("""
                CREATE PROCEDURE telemetry.usp_RefreshRollups
                    @MaxBuckets int = 5000
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    DECLARE @work TABLE (SensorId varchar(32) NOT NULL, BucketStart datetime2(3) NOT NULL, PRIMARY KEY (SensorId, BucketStart));

                    BEGIN TRAN;

                    DELETE TOP (@MaxBuckets) FROM telemetry.RollupDirty WITH (READPAST)
                    OUTPUT deleted.SensorId, deleted.BucketStart INTO @work;

                    MERGE telemetry.SensorReadings1m WITH (HOLDLOCK) AS t
                    USING (
                        SELECT w.SensorId, w.BucketStart,
                               COUNT(*)                      AS ReadingCount,
                               SUM(r.Temperature)            AS SumTemperature,
                               MIN(r.Temperature)            AS MinTemperature,
                               MAX(r.Temperature)            AS MaxTemperature,
                               SUM(r.Humidity)               AS SumHumidity,
                               MIN(r.Humidity)               AS MinHumidity,
                               MAX(r.Humidity)               AS MaxHumidity,
                               SUM(CAST(r.DoorOpen AS int))  AS DoorOpenCount,
                               SUM(CAST(r.IsAnomaly AS int)) AS AnomalyCount
                        FROM @work w
                        JOIN telemetry.SensorReadings r
                          ON r.SensorId = w.SensorId
                         AND r.[Timestamp] >= w.BucketStart
                         AND r.[Timestamp] < DATEADD(minute, 1, w.BucketStart)
                        GROUP BY w.SensorId, w.BucketStart
                    ) AS s
                    ON t.SensorId = s.SensorId AND t.BucketStart = s.BucketStart
                    WHEN MATCHED THEN UPDATE SET
                        ReadingCount = s.ReadingCount, SumTemperature = s.SumTemperature,
                        MinTemperature = s.MinTemperature, MaxTemperature = s.MaxTemperature,
                        SumHumidity = s.SumHumidity, MinHumidity = s.MinHumidity, MaxHumidity = s.MaxHumidity,
                        DoorOpenCount = s.DoorOpenCount, AnomalyCount = s.AnomalyCount, RefreshedAt = SYSUTCDATETIME()
                    WHEN NOT MATCHED BY TARGET THEN INSERT
                        (SensorId, BucketStart, ReadingCount, SumTemperature, MinTemperature, MaxTemperature,
                         SumHumidity, MinHumidity, MaxHumidity, DoorOpenCount, AnomalyCount)
                    VALUES
                        (s.SensorId, s.BucketStart, s.ReadingCount, s.SumTemperature, s.MinTemperature, s.MaxTemperature,
                         s.SumHumidity, s.MinHumidity, s.MaxHumidity, s.DoorOpenCount, s.AnomalyCount);

                    COMMIT;

                    SELECT COUNT(*) AS Refreshed FROM @work;
                END;
                """);

            // Backfill: mark every minute that already has readings.
            migrationBuilder.Sql("""
                INSERT telemetry.RollupDirty (SensorId, BucketStart)
                SELECT DISTINCT SensorId, DATE_BUCKET(minute, 1, [Timestamp]) FROM telemetry.SensorReadings;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE telemetry.usp_RefreshRollups;");
            migrationBuilder.Sql("DROP TABLE telemetry.RollupDirty;");
            migrationBuilder.Sql("DROP TABLE telemetry.SensorReadings1m;");
        }
    }
}
