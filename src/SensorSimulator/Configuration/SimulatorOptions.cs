using Microsoft.Extensions.Options;
using SensorSimulator.Topology;

namespace SensorSimulator.Configuration;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>Where the warehouses and sensors come from; the API by default.</summary>
    public TopologySourceKind TopologySource { get; set; } = TopologySourceKind.Api;

    /// <summary>Base address of SensorDashboard.Api (ingestion and topology).</summary>
    public string IngestBaseUrl { get; set; } = "http://localhost:5278";

    public string IngestPath { get; set; } = "/ingest";

    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Chance per sensor per tick that a new anomaly starts.</summary>
    public double AnomalyProbability { get; set; } = 0.02;

    public int MinAnomalyTicks { get; set; } = 3;

    public int MaxAnomalyTicks { get; set; } = 10;

    public double MinTemperatureF { get; set; } = 34;

    public double MaxTemperatureF { get; set; } = 40;

    /// <summary>
    /// Store-and-forward capacity: batches held while the API is unreachable (including the
    /// newest). 720 is one hour at the default 5 s interval; the oldest go first when full.
    /// </summary>
    public int MaxBufferedBatches { get; set; } = 720;

    /// <summary>Set to make a run reproducible; leave null for a random run.</summary>
    public int? RandomSeed { get; set; }
}

public sealed class SimulatorOptionsValidator : IValidateOptions<SimulatorOptions>
{
    public ValidateOptionsResult Validate(string? name, SimulatorOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.IngestBaseUrl, UriKind.Absolute, out _))
            failures.Add($"{nameof(options.IngestBaseUrl)} must be an absolute URL.");
        if (options.IntervalSeconds < 1)
            failures.Add($"{nameof(options.IntervalSeconds)} must be at least 1.");
        if (options.AnomalyProbability is < 0 or > 1)
            failures.Add($"{nameof(options.AnomalyProbability)} must be between 0 and 1.");
        if (options.MinAnomalyTicks < 1 || options.MaxAnomalyTicks < options.MinAnomalyTicks)
            failures.Add($"Require 1 <= {nameof(options.MinAnomalyTicks)} <= {nameof(options.MaxAnomalyTicks)}.");
        if (options.MaxBufferedBatches < 1)
            failures.Add($"{nameof(options.MaxBufferedBatches)} must be at least 1.");
        if (options.MinTemperatureF >= options.MaxTemperatureF)
            failures.Add($"{nameof(options.MinTemperatureF)} must be less than {nameof(options.MaxTemperatureF)}.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
