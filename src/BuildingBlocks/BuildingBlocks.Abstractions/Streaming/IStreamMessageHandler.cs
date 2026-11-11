namespace BuildingBlocks.Abstractions.Streaming;

/// <summary>
/// Handles one message type read from an event stream (Kafka topic). Resolved from a new DI scope per message.
/// Messages of a partition are handled one at a time, in order.
/// </summary>
/// <remarks>
/// Complete normally and the message's offset is committed. Throw and the message is retried in-process with
/// backoff; after the last attempt it is sent to the consumer group's dead-letter topic and the offset is committed,
/// so one bad message does not block the partition forever. Must be idempotent: after a crash or rebalance,
/// messages since the last commit are delivered again.
/// </remarks>
/// <typeparam name="TMessage">The message type.</typeparam>
public interface IStreamMessageHandler<TMessage>
    where TMessage : class
{
    /// <summary>Handles the message.</summary>
    /// <param name="context">The message and its stream metadata.</param>
    /// <param name="cancellationToken">Cancelled only if graceful shutdown runs out of time.</param>
    /// <returns>A task that completes when the message has been handled.</returns>
    Task HandleAsync(StreamMessageContext<TMessage> context, CancellationToken cancellationToken);
}
