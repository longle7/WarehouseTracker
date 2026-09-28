using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Telemetry;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("IoTDigitalTwin")
    ?? throw new InvalidOperationException("Connection string 'IoTDigitalTwin' is not configured.");

builder.Services.AddControllers();
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
