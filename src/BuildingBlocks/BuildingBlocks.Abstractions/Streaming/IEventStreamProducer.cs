namespace BuildingBlocks.Abstractions.Streaming;

/// <summary>
/// Appends messages to an event stream (Kafka topic). Technology-agnostic: Application code depends on this
/// interface, never on the Kafka client. Messages with the same key go to the same partition, so they are
/// consumed in the order they were produced.
/// </summary>
/// <remarks>
/// Use event streams for ordered, replayable streams of facts (e.g. change feeds, analytics, audit). Use
/// <c>IMessagePublisher</c> (RabbitMQ) for integration events and work that needs per-message acknowledgement.
/// See docs/streaming.md.
/// </remarks>
public interface IEventStreamProducer
{
    /// <summary>
    /// Produces <paramref name="message"/> to <paramref name="topic"/> and completes once the broker has
    /// acknowledged it (all in-sync replicas). The client retries transient failures itself. Delivery is
    /// exactly-once per producer session (idempotent producer), at-least-once across restarts.
    /// </summary>
    /// <typeparam name="TMessage">The message type, serialized as JSON.</typeparam>
    /// <param name="topic">The topic, e.g. <c>orders.order-events</c>.</param>
    /// <param name="key">
    /// The partition key (e.g. an aggregate ID): same key, same partition, same order. <see langword="null"/>
    /// spreads messages over partitions without ordering guarantees.
    /// </param>
    /// <param name="message">The message. Its <c>EventId</c> becomes the message ID for integration events.</param>
    /// <param name="correlationId">The correlation ID to carry along, if any.</param>
    /// <param name="cancellationToken">
    /// Stops waiting for the acknowledgement. The message may still be delivered: cancellation cannot recall it.
    /// </param>
    /// <returns>A task that completes when the broker has acknowledged the message.</returns>
    /// <exception cref="StreamProduceException">The message could not be delivered.</exception>
    Task ProduceAsync<TMessage>(
        string topic,
        string? key,
        TMessage message,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;
}
