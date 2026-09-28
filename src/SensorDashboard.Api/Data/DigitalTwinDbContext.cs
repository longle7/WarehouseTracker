using Microsoft.EntityFrameworkCore;
using SensorDashboard.Api.Data.Metadata;

namespace SensorDashboard.Api.Data;

/// <summary>
/// Slow-changing metadata (the DynamoDB side of the AWS design). Readings live in the
/// partitioned telemetry.SensorReadings table, which is created by raw SQL in the
/// TelemetryStore migration and written through <see cref="Telemetry.IReadingWriter"/>.
/// </summary>
public sealed class DigitalTwinDbContext(DbContextOptions<DigitalTwinDbContext> options) : DbContext(options)
{
    public const string MetadataSchema = "metadata";
    public const int IdMaxLength = 32;

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Sensor> Sensors => Set<Sensor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MetadataSchema);

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.Property(w => w.Id).HasMaxLength(IdMaxLength).IsUnicode(false);
            b.Property(w => w.Name).HasMaxLength(200);
            b.Property(w => w.City).HasMaxLength(100);
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Warehouses_Latitude", "[Latitude] BETWEEN -90 AND 90");
                t.HasCheckConstraint("CK_Warehouses_Longitude", "[Longitude] BETWEEN -180 AND 180");
            });
            b.HasData(MetadataSeed.Warehouses);
        });

        modelBuilder.Entity<Sensor>(b =>
        {
            // varchar(32) to match telemetry.SensorReadings.SensorId / WarehouseId.
            b.Property(s => s.Id).HasMaxLength(IdMaxLength).IsUnicode(false);
            b.Property(s => s.WarehouseId).HasMaxLength(IdMaxLength).IsUnicode(false);
            b.Property(s => s.Location).HasMaxLength(200);
            b.Property(s => s.MinTemperatureF).HasPrecision(5, 2);
            b.Property(s => s.MaxTemperatureF).HasPrecision(5, 2);
            b.ToTable(t => t.HasCheckConstraint("CK_Sensors_TemperatureRange", "[MinTemperatureF] < [MaxTemperatureF]"));
            b.HasOne(s => s.Warehouse).WithMany(w => w.Sensors).HasForeignKey(s => s.WarehouseId);
            b.HasData(MetadataSeed.Sensors);
        });
    }
}
