using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorDashboard.Api.Data.Migrations
{
    /// <summary>
    /// Health check results recorded by the HealthSampler every sample interval, for uptime
    /// percentages and incident history (GET /status). Kept for 30 days.
    /// </summary>
    public partial class HealthSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE ops.HealthSamples
                (
                    Id         bigint IDENTITY NOT NULL CONSTRAINT PK_HealthSamples PRIMARY KEY,
                    CheckedAt  datetime2(3)    NOT NULL,
                    Status     varchar(16)     NOT NULL,
                    DurationMs int             NOT NULL,
                    Failing    nvarchar(1000)  NOT NULL
                );
                """);

            // Uptime windows and incident detection scan by time.
            migrationBuilder.Sql("CREATE INDEX IX_HealthSamples_CheckedAt ON ops.HealthSamples (CheckedAt) INCLUDE (Status);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE ops.HealthSamples;");
        }
    }
}
