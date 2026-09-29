using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Outcome of each processed ingest batch, written in the same transaction as its readings,
    /// so GET /ingest/batches/{id} can report what happened after the 202.
    /// </summary>
    public partial class IngestBatchResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE ingest.BatchResults
                (
                    BatchId     bigint       NOT NULL CONSTRAINT PK_BatchResults PRIMARY KEY,
                    EnqueuedAt  datetime2(3) NOT NULL,
                    ProcessedAt datetime2(3) NOT NULL CONSTRAINT DF_BatchResults_ProcessedAt DEFAULT SYSUTCDATETIME(),
                    Attempts    int          NOT NULL,
                    Received    int          NOT NULL,
                    Inserted    int          NOT NULL,
                    Duplicates  int          NOT NULL,
                    Rejected    int          NOT NULL
                );
                """);

            // Retention trims by age.
            migrationBuilder.Sql("CREATE INDEX IX_BatchResults_ProcessedAt ON ingest.BatchResults (ProcessedAt);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE ingest.BatchResults;");
        }
    }
}
