using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Stores ingest.ReadingBatches.AvailableAt at full precision. As datetime2(3), the
    /// SYSUTCDATETIME() default is rounded to the nearest millisecond, which puts it in the
    /// future about half the time: a just-enqueued batch then fails "AvailableAt &lt;= now", the
    /// consumer's immediate wake-up misses it, and it waits for the next poll (up to 1 s).
    /// </summary>
    public partial class QueueAvailableAtPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_ReadingBatches_AvailableAt ON ingest.ReadingBatches;");
            migrationBuilder.Sql("ALTER TABLE ingest.ReadingBatches ALTER COLUMN AvailableAt datetime2(7) NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX IX_ReadingBatches_AvailableAt ON ingest.ReadingBatches (AvailableAt, Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_ReadingBatches_AvailableAt ON ingest.ReadingBatches;");
            migrationBuilder.Sql("ALTER TABLE ingest.ReadingBatches ALTER COLUMN AvailableAt datetime2(3) NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX IX_ReadingBatches_AvailableAt ON ingest.ReadingBatches (AvailableAt, Id);");
        }
    }
}
