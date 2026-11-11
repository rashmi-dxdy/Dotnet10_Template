namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// Handles one message type received from the broker. Implementations live in the consuming module and are
/// resolved from a new DI scope per message. They must be idempotent: a message can be delivered more than once.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
public interface IMessageHandler<TMessage>
    where TMessage : class
{
    /// <summary>Handles the message and tells the broker what to do with it.</summary>
    /// <param name="context">The message and its delivery metadata.</param>
    /// <param name="cancellationToken">Cancelled only if graceful shutdown runs out of time.</param>
    /// <returns>
    /// <see cref="MessageHandlingResult.Ack"/> when done, <see cref="MessageHandlingResult.Retry"/> to have it
    /// redelivered, or <see cref="MessageHandlingResult.Reject"/> to dead-letter it. An unhandled exception is
    /// treated according to the <c>RequeueOnException</c> setting.
    /// </returns>
    Task<MessageHandlingResult> HandleAsync(MessageContext<TMessage> context, CancellationToken cancellationToken);
}
