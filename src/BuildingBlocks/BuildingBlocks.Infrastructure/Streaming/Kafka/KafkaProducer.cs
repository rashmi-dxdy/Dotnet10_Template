using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Streaming;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// <see cref="IEventStreamProducer"/> for Kafka. ONE producer per service (thread-safe, keeps its broker
/// connections), configured for reliability: <c>acks=all</c>, idempotence (no duplicates or reordering from
/// client retries), automatic retries until <c>Producer:MessageTimeoutMs</c>. Flushed on shutdown.
/// </summary>
internal sealed class KafkaProducer : IEventStreamProducer, IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProducer<string?, byte[]> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IOptions<KafkaOptions> options, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var events = new KafkaClientEvents(logger, "producer");
        _producer = new ProducerBuilder<string?, byte[]>(KafkaClientConfiguration.Producer(options.Value))
            .SetErrorHandler((_, error) => events.OnError(error))
            .SetLogHandler((_, message) => events.OnLog(message))
            .Build();
    }

    /// <inheritdoc />
    public async Task ProduceAsync<TMessage>(
        string topic,
        string? key,
        TMessage message,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);

        var messageType = typeof(TMessage).Name;
        var messageId = (message as IIntegrationEvent)?.EventId.ToString() ?? Guid.NewGuid().ToString();
        var headers = new Headers();
        KafkaHeaders.Set(headers, KafkaHeaders.MessageId, messageId);
        KafkaHeaders.Set(headers, KafkaHeaders.CorrelationId, correlationId);
        KafkaHeaders.Set(headers, KafkaHeaders.MessageType, messageType);
        KafkaHeaders.Set(headers, KafkaHeaders.ContentType, "application/json");

        var kafkaMessage = new Message<string?, byte[]>
        {
            Key = key,
            Value = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions),
            Headers = headers,
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
            KafkaLog.Produced(_logger, messageType, messageId, topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string?, byte[]> exception)
        {
            throw new StreamProduceException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Producing {messageType} {messageId} to Kafka topic '{topic}' failed: {exception.Error.Reason} "
                    + $"({exception.Error.Code})."),
                exception);
        }
    }

    /// <summary>Produces an already-serialized message (used for dead-lettering).</summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">A token to stop waiting for the acknowledgement.</param>
    /// <returns>The delivery result.</returns>
    public Task<DeliveryResult<string?, byte[]>> ProduceRawAsync(
        string topic,
        Message<string?, byte[]> message,
        CancellationToken cancellationToken) =>
        _producer.ProduceAsync(topic, message, cancellationToken);

    /// <summary>Waits until all in-flight messages are acknowledged or <paramref name="timeout"/> elapses.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>How many messages are still not acknowledged.</returns>
    public int Flush(TimeSpan timeout) => _producer.Flush(timeout);

    /// <inheritdoc />
    public void Dispose() => _producer.Dispose();
}
