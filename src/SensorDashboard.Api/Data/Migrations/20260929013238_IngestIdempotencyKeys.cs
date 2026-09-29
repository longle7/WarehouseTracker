using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Idempotency keys for POST /ingest: a retried request with the same key gets the original
    /// batch back instead of queuing a duplicate. Kept for a day (trimmed by the queue consumer).
    /// </summary>
    public partial class IngestIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE ingest.IdempotencyKeys
                (
                    [Key]     nvarchar(100) NOT NULL CONSTRAINT PK_IdempotencyKeys PRIMARY KEY,
                    BatchId   bigint        NOT NULL,
                    CreatedAt datetime2(3)  NOT NULL CONSTRAINT DF_IdempotencyKeys_CreatedAt DEFAULT SYSUTCDATETIME()
                );
                """);
            migrationBuilder.Sql("CREATE INDEX IX_IdempotencyKeys_CreatedAt ON ingest.IdempotencyKeys (CreatedAt);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE ingest.IdempotencyKeys;");
        }
    }
}
