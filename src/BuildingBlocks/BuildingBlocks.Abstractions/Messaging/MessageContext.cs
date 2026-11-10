namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// A received message with its delivery metadata.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <param name="Message">The deserialized message.</param>
/// <param name="MessageId">The message ID (the event ID for integration events). Use it to deduplicate.</param>
/// <param name="CorrelationId">The correlation ID the publisher sent, if any.</param>
/// <param name="Topic">The topic the message was published under.</param>
/// <param name="Redelivered">Whether the broker delivered this message before (a previous attempt failed).</param>
/// <param name="DeliveryCount">How many earlier delivery attempts failed; <c>0</c> on the first delivery.</param>
public sealed record MessageContext<TMessage>(
    TMessage Message,
    string MessageId,
    string? CorrelationId,
    string Topic,
    bool Redelivered,
    int DeliveryCount)
    where TMessage : class;
