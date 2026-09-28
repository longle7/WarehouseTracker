using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Durable ingestion queue and dead-letter table (the SQS queue + DLQ of the AWS design).
    /// Hand-written and not mapped in EF: SqlReadingQueue owns all access.
    /// </summary>
    public partial class IngestQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA ingest;");

            migrationBuilder.Sql("""
                CREATE TABLE ingest.ReadingBatches
                (
                    Id           bigint IDENTITY  NOT NULL CONSTRAINT PK_ReadingBatches PRIMARY KEY,
                    EnqueuedAt   datetime2(3)     NOT NULL CONSTRAINT DF_ReadingBatches_EnqueuedAt DEFAULT SYSUTCDATETIME(),
                    -- Not visible to consumers before this time (retry backoff).
                    AvailableAt  datetime2(3)     NOT NULL CONSTRAINT DF_ReadingBatches_AvailableAt DEFAULT SYSUTCDATETIME(),
                    Attempts     int              NOT NULL CONSTRAINT DF_ReadingBatches_Attempts DEFAULT 0,
                    ReadingCount int              NOT NULL,
                    Payload      nvarchar(max)    NOT NULL,
                    LastError    nvarchar(2000)   NULL
                );
                """);

            // Consumers scan for the oldest due batch.
            migrationBuilder.Sql("CREATE INDEX IX_ReadingBatches_AvailableAt ON ingest.ReadingBatches (AvailableAt, Id);");

            migrationBuilder.Sql("""
                CREATE TABLE ingest.DeadLetters
                (
                    Id             bigint IDENTITY NOT NULL CONSTRAINT PK_DeadLetters PRIMARY KEY,
                    BatchId        bigint          NOT NULL,
                    EnqueuedAt     datetime2(3)    NOT NULL,
                    DeadLetteredAt datetime2(3)    NOT NULL CONSTRAINT DF_DeadLetters_DeadLetteredAt DEFAULT SYSUTCDATETIME(),
                    Attempts       int             NOT NULL,
                    ReadingCount   int             NOT NULL,
                    Payload        nvarchar(max)   NOT NULL,
                    Error          nvarchar(2000)  NOT NULL
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE ingest.DeadLetters;");
            migrationBuilder.Sql("DROP TABLE ingest.ReadingBatches;");
            migrationBuilder.Sql("DROP SCHEMA ingest;");
        }
    }
}
