namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// Publishes messages to the message broker. Technology-agnostic: Application code depends on this interface,
/// never on the broker client. The infrastructure implementation (RabbitMQ) makes messages durable and waits
/// for the broker to confirm them.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes <paramref name="message"/> under <paramref name="topic"/> and completes once the broker has
    /// confirmed it. Transient broker failures are retried; delivery is at-least-once, so consumers must be
    /// idempotent.
    /// </summary>
    /// <typeparam name="TMessage">The message type, serialized as JSON.</typeparam>
    /// <param name="topic">
    /// The routing topic, e.g. <c>orders.order-placed</c> (<c>&lt;publishing-service&gt;.&lt;event&gt;</c>).
    /// </param>
    /// <param name="message">The message. Its <see cref="IIntegrationEvent.EventId"/> becomes the message ID.</param>
    /// <param name="correlationId">The correlation ID to carry along, if any.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the broker has confirmed the message.</returns>
    /// <exception cref="MessagePublishException">The message could not be published.</exception>
    Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;
}
