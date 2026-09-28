using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Data.Ingestion;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;
using SensorDashboard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("IoTDigitalTwin")
    ?? throw new InvalidOperationException("Connection string 'IoTDigitalTwin' is not configured.");

// Enums as camelCase strings, e.g. SensorStatus.Offline -> "offline", for REST and SignalR alike.
var enumConverter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(enumConverter));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton(TimeProvider.System);

// Metadata: EF Core.
builder.Services.AddDbContext<DigitalTwinDbContext>(o => o.UseSqlServer(connectionString));

// Telemetry write path: raw ADO.NET with a table-valued parameter.
builder.Services.AddOptions<TelemetryOptions>().BindConfiguration(TelemetryOptions.SectionName);
builder.Services.AddSingleton<IReadingWriter>(_ => new SqlReadingWriter(connectionString));
builder.Services.AddHostedService(sp => new PartitionMaintenanceService(
    connectionString,
    sp.GetRequiredService<IOptions<TelemetryOptions>>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<PartitionMaintenanceService>>()));
builder.Services.AddHostedService(sp => new RollupService(
    connectionString,
    sp.GetRequiredService<IOptions<TelemetryOptions>>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<RollupService>>()));

// Ingestion queue: POST /ingest enqueues; QueuedIngestProcessor writes (SQS + Lambda analog).
builder.Services.AddOptions<IngestionOptions>().BindConfiguration(IngestionOptions.SectionName);
builder.Services.AddSingleton(sp => new SqlReadingQueue(
    connectionString, sp.GetRequiredService<IOptions<IngestionOptions>>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddHostedService(sp => new QueuedIngestProcessor(
    connectionString,
    sp.GetRequiredService<SqlReadingQueue>(),
    sp.GetRequiredService<IngestMetrics>(),
    sp.GetRequiredService<ILiveUpdateNotifier>(),
    sp.GetRequiredService<IOptions<IngestionOptions>>(),
    sp.GetRequiredService<ILogger<QueuedIngestProcessor>>()));

// Read path: metadata from EF Core, time series from Dapper, joined in DashboardService.
builder.Services.AddOptions<DashboardOptions>().BindConfiguration(DashboardOptions.SectionName);
builder.Services.AddSingleton<IReadingQueries>(_ => new SqlReadingQueries(connectionString));
builder.Services.AddScoped<DashboardService>();

// Live push: SignalR hub fed by a broadcaster that ingestion notifies.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(enumConverter));
builder.Services.AddSingleton<LiveConnectionTracker>();
builder.Services.AddSingleton<LiveUpdateBroadcaster>();
builder.Services.AddSingleton<ILiveUpdateNotifier>(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());

// Alert lifecycle: debounced alerts evaluated from stored readings, notified via a pluggable sink.
builder.Services.AddOptions<AlertOptions>().BindConfiguration(AlertOptions.SectionName);
builder.Services.AddScoped<AlertService>();
builder.Services.AddSingleton<IAlertNotificationSink, LoggingAlertNotificationSink>();
builder.Services.AddHostedService<AlertEvaluator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
