namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// What the broker should do with a message after its handler ran.
/// </summary>
public enum MessageHandlingResult
{
    /// <summary>Processed: acknowledge and remove it from the queue.</summary>
    Ack = 0,

    /// <summary>
    /// Temporary failure: negatively acknowledge and requeue it for another attempt. After the queue's
    /// delivery limit it is dead-lettered instead, so a failing message cannot loop forever.
    /// </summary>
    Retry = 1,

    /// <summary>Permanent failure (e.g. invalid content): negatively acknowledge without requeue (dead-letter).</summary>
    Reject = 2,
}
