namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// A message could not be published (broker unavailable, not confirmed, or unroutable) after all retries.
/// Lets Application code react to publishing failures without depending on the broker client.
/// </summary>
public sealed class MessagePublishException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MessagePublishException"/> class.</summary>
    public MessagePublishException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MessagePublishException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public MessagePublishException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MessagePublishException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying broker error.</param>
    public MessagePublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
