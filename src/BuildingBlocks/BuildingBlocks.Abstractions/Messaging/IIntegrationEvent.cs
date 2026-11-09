namespace BuildingBlocks.Abstractions.Messaging;

/// <summary>
/// A message published by one service for other services, via the message broker.
/// Integration events are public contracts: keep them small, serializable and backward compatible.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Gets the unique identifier of this message, used for idempotent consumption.</summary>
    Guid EventId { get; }

    /// <summary>Gets the moment the event occurred (UTC).</summary>
    DateTimeOffset OccurredOn { get; }
}
