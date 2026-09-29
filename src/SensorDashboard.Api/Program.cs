using IoTDigitalTwin.Contracts;
using Microsoft.EntityFrameworkCore;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Data.Ingestion;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;
using SensorDashboard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("IoTDigitalTwin")
    ?? throw new InvalidOperationException("Connection string 'IoTDigitalTwin' is not configured.");

// Contract wire format (camelCase enums) for REST; SignalR below uses the same.
builder.Services.AddControllers()
    .AddJsonOptions(o => ContractJson.Configure(o.JsonSerializerOptions));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));

// Metadata: EF Core.
builder.Services.AddDbContext<DigitalTwinDbContext>(o => o.UseSqlServer(connectionString));

// Telemetry write path: raw ADO.NET with a table-valued parameter.
builder.Services.AddOptions<TelemetryOptions>().BindConfiguration(TelemetryOptions.SectionName);
builder.Services.AddSingleton<IReadingWriter, SqlReadingWriter>();
builder.Services.AddHostedService<PartitionMaintenanceService>();
builder.Services.AddHostedService<RollupService>();

// Ingestion queue: POST /ingest enqueues; QueuedIngestProcessor writes (SQS + Lambda analog).
builder.Services.AddOptions<IngestionOptions>().BindConfiguration(IngestionOptions.SectionName);
builder.Services.AddSingleton<SqlReadingQueue>();
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddHostedService<QueuedIngestProcessor>();

// Read path: metadata from EF Core, time series from Dapper, joined in DashboardService.
builder.Services.AddOptions<DashboardOptions>().BindConfiguration(DashboardOptions.SectionName);
builder.Services.AddSingleton<IReadingQueries, SqlReadingQueries>();
builder.Services.AddScoped<DashboardService>();

// Live push: SignalR hub fed by a broadcaster that ingestion notifies.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => ContractJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddSingleton<LiveConnectionTracker>();
builder.Services.AddSingleton<LiveUpdateBroadcaster>();
builder.Services.AddSingleton<ILiveUpdateNotifier>(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());

// Alert lifecycle: debounced alerts evaluated from stored readings, notified via a pluggable sink.
builder.Services.AddOptions<AlertOptions>().BindConfiguration(AlertOptions.SectionName);
builder.Services.AddScoped<AlertService>();
builder.Services.AddSingleton<IAlertNotificationSink, LoggingAlertNotificationSink>();
builder.Services.AddHostedService<AlertEvaluator>();

// Readiness for containers and CI: the database answers and is migrated.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
