namespace BuildingBlocks.Abstractions.Domain;

/// <summary>
/// Something that happened inside a module's domain. Raised by aggregates and handled
/// in-process within the SAME service. Never sent to other services directly.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event occurrence.</summary>
    Guid EventId { get; }

    /// <summary>Gets the moment the event occurred (UTC).</summary>
    DateTimeOffset OccurredOn { get; }
}
