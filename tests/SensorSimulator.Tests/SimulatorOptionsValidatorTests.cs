using SensorSimulator.Configuration;

namespace SensorSimulator.Tests;

public class SimulatorOptionsValidatorTests
{
    private readonly SimulatorOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid() => Assert.True(_validator.Validate(null, new SimulatorOptions()).Succeeded);

    public static TheoryData<string, Action<SimulatorOptions>> InvalidOptions => new()
    {
        { "IngestBaseUrl", o => o.IngestBaseUrl = "not a url" },
        { "IntervalSeconds", o => o.IntervalSeconds = 0 },
        { "AnomalyProbability", o => o.AnomalyProbability = 1.5 },
        { "MinAnomalyTicks", o => o.MaxAnomalyTicks = 1 },
        { "MinTemperatureF", o => o.MinTemperatureF = 45 },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_values_fail_with_a_message_naming_the_setting(string setting, Action<SimulatorOptions> breakIt)
    {
        var options = new SimulatorOptions();
        breakIt(options);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage);
    }
}
