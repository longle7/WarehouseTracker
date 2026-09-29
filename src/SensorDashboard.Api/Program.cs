using IoTDigitalTwin.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Data.Ingestion;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Monitoring;
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

// Rate limits, request body cap, request timeouts and ProblemDetails for every failure.
builder.AddApiProtection();

// Contract wire format (camelCase enums) for REST; SignalR below uses the same.
builder.Services.AddControllers()
    .AddJsonOptions(o => ContractJson.Configure(o.JsonSerializerOptions));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new SqlConnectionFactory(
    connectionString, sp.GetRequiredService<IOptions<ApiLimitsOptions>>().Value.SqlCommandTimeoutSeconds));

// Metadata: EF Core, retrying transient SQL errors with a short budget so requests don't hang.
builder.Services.AddDbContext<DigitalTwinDbContext>((sp, o) => o.UseSqlServer(connectionString, sql => sql
    .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)
    .CommandTimeout(sp.GetRequiredService<IOptions<ApiLimitsOptions>>().Value.SqlCommandTimeoutSeconds)));

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

// Read path: metadata from EF Core, time series from Dapper, joined in DashboardService.
builder.Services.AddValidatedOptions<DashboardOptions>(DashboardOptions.SectionName)
    .Validate(o => o.DefaultHistoryWindow <= o.MaxHistoryWindow, "Dashboard:DefaultHistoryWindow must not exceed MaxHistoryWindow.");
builder.Services.AddSingleton<IReadingQueries, SqlReadingQueries>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MetadataCache>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddApiOutputCache();

// Live push: SignalR hub fed by a broadcaster that ingestion notifies.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => ContractJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddSingleton<LiveConnectionTracker>();
builder.Services.AddSingleton<SubscriptionRegistry>();
builder.Services.AddSingleton<LiveUpdateBroadcaster>();
builder.Services.AddSingleton<ILiveUpdateNotifier>(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveUpdateBroadcaster>());

// Alert lifecycle: debounced alerts evaluated from stored readings, notified via a pluggable sink.
builder.Services.AddValidatedOptions<AlertOptions>(AlertOptions.SectionName);
builder.Services.AddScoped<AlertService>();
builder.Services.AddSingleton<IAlertNotificationSink, LoggingAlertNotificationSink>();
builder.Services.AddHostedService<AlertEvaluator>();

// Health: readiness (database), pipeline health (queue backlog) and data freshness. The
// sampler records every result for uptime history and logs state changes; GET /status reports it.
var checkTimeout = TimeSpan.FromSeconds(5);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", timeout: checkTimeout)
    .AddCheck<IngestQueueHealthCheck>("ingest-queue", timeout: checkTimeout)
    .AddCheck<DataFreshnessHealthCheck>("data-freshness", timeout: checkTimeout);
builder.Services.AddValidatedOptions<MonitoringOptions>(MonitoringOptions.SectionName);
builder.Services.AddOptions<HealthCheckPublisherOptions>().Configure<IOptions<MonitoringOptions>>((o, monitoring) =>
{
    o.Delay = TimeSpan.FromSeconds(5);
    o.Period = monitoring.Value.SampleInterval;
});
builder.Services.AddSingleton<IHealthCheckPublisher, HealthSampler>();
builder.Services.AddSingleton<StatusService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseApiProtection();
app.UseOutputCache();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
// Hub connections are long-lived by design; request timeouts don't apply.
app.MapHub<TelemetryHub>("/hubs/telemetry").DisableRequestTimeout();

app.Run();
return 0;

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
