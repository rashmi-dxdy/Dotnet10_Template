namespace BuildingBlocks.Abstractions.Streaming;

/// <summary>
/// A message could not be delivered to the event stream (broker unreachable, timed out, or rejected).
/// Lets Application code react without depending on the Kafka client.
/// </summary>
public sealed class StreamProduceException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StreamProduceException"/> class.</summary>
    public StreamProduceException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StreamProduceException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public StreamProduceException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StreamProduceException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying client error.</param>
    public StreamProduceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
