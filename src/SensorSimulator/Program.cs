using Microsoft.Extensions.Options;
using SensorSimulator;
using SensorSimulator.Configuration;
using SensorSimulator.Generation;
using SensorSimulator.Publishing;
using SensorSimulator.Topology;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<SimulatorOptions>()
    .BindConfiguration(SimulatorOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SimulatorOptions>, SimulatorOptionsValidator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IReadOnlyList<Warehouse>>(WarehouseTopology.Seed);
builder.Services.AddSingleton<ReadingGenerator>();

// Standard pipeline (Polly v8): rate limiter, total timeout, retry with exponential
// backoff + jitter, circuit breaker, per-attempt timeout.
builder.Services.AddHttpClient(HttpReadingPublisher.HttpClientName, (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.IngestBaseUrl))
    .AddStandardResilienceHandler(o =>
    {
        // The defaults suit high-traffic clients: the breaker needs 100 requests per 30s
        // window before it can trip, which one-batch-per-tick never reaches.
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        o.Retry.Delay = TimeSpan.FromMilliseconds(500);
    });
builder.Services.AddSingleton<IReadingPublisher, HttpReadingPublisher>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
