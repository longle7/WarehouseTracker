using IoTDigitalTwin.Contracts.Telemetry;

namespace SensorSimulator.Publishing;

/// <summary>
/// Transport for a tick's readings. HTTP today; a RabbitMQ or Service Bus
/// implementation can replace it without touching the worker.
/// </summary>
public interface IReadingPublisher
{
    Task PublishAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken);
}
