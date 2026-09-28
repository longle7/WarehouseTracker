using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
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
