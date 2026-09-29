using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTDigitalTwin.Contracts;

/// <summary>
/// The wire format of every contract: web defaults (camelCase properties) with enums as
/// camelCase strings, e.g. SensorStatus.Offline -> "offline". Shared by the API (REST and
/// SignalR), the simulator and the tests so producer and consumer can't drift apart.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the contract conventions to options owned by a framework (MVC, SignalR).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
