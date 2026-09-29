using IoTDigitalTwin.Contracts.Telemetry;

namespace SensorSimulator.Publishing;

/// <summary>
/// Transport for a tick's readings. HTTP today; a RabbitMQ or Service Bus
/// implementation can replace it without touching the worker.
/// </summary>
public interface IReadingPublisher
{
    /// <param name="idempotencyKey">Identifies the batch; every resend of it carries the same key.</param>
    Task PublishAsync(IReadOnlyList<SensorReadingDto> readings, string idempotencyKey, CancellationToken cancellationToken);
}
