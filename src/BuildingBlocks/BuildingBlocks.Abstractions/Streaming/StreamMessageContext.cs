namespace BuildingBlocks.Abstractions.Streaming;

/// <summary>
/// A message read from an event stream, with its position in the stream.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <param name="Message">The deserialized message.</param>
/// <param name="MessageId">The message ID (the event ID for integration events). Use it to deduplicate.</param>
/// <param name="CorrelationId">The correlation ID the producer sent, if any.</param>
/// <param name="Key">The partition key, if any.</param>
/// <param name="Topic">The topic.</param>
/// <param name="Partition">The partition the message was read from.</param>
/// <param name="Offset">The message's offset within the partition.</param>
/// <param name="Timestamp">When the message was produced (or appended, depending on the topic).</param>
/// <param name="Attempt">The processing attempt in this consumer: <c>1</c> first, higher on in-process retries.</param>
public sealed record StreamMessageContext<TMessage>(
    TMessage Message,
    string MessageId,
    string? CorrelationId,
    string? Key,
    string Topic,
    int Partition,
    long Offset,
    DateTimeOffset Timestamp,
    int Attempt)
    where TMessage : class;
