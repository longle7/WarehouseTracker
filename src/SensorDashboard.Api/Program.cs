using IoTDigitalTwin.Contracts;
using Microsoft.EntityFrameworkCore;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Data.Ingestion;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;
using SensorDashboard.Api.Services;

// Container health probe (`dotnet SensorDashboard.Api.dll --healthcheck`): the runtime image has
// no curl, so the app checks its own /health endpoint and exits 0 (healthy) or 1.
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    try
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        return (await probe.GetAsync($"http://localhost:{port}/health")).IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

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

// Metadata: EF Core, retrying transient SQL errors with a short budget so requests don't hang.
builder.Services.AddDbContext<DigitalTwinDbContext>(o => o.UseSqlServer(connectionString, sql =>
    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)));

// Telemetry write path: raw ADO.NET with a table-valued parameter.
builder.Services.AddValidatedOptions<TelemetryOptions>(TelemetryOptions.SectionName);
builder.Services.AddSingleton<IReadingWriter, SqlReadingWriter>();
builder.Services.AddHostedService<PartitionMaintenanceService>();
builder.Services.AddHostedService<RollupService>();

// Ingestion queue: POST /ingest enqueues; QueuedIngestProcessor writes (SQS + Lambda analog).
builder.Services.AddValidatedOptions<IngestionOptions>(IngestionOptions.SectionName);
builder.Services.AddSingleton<SqlReadingQueue>();
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddHostedService<QueuedIngestProcessor>();
builder.Services.AddIngestRateLimiting();

// Read path: metadata from EF Core, time series from Dapper, joined in DashboardService.
builder.Services.AddValidatedOptions<DashboardOptions>(DashboardOptions.SectionName)
    .Validate(o => o.DefaultHistoryWindow <= o.MaxHistoryWindow, "Dashboard:DefaultHistoryWindow must not exceed MaxHistoryWindow.");
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
builder.Services.AddValidatedOptions<AlertOptions>(AlertOptions.SectionName);
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

app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
return 0;

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
