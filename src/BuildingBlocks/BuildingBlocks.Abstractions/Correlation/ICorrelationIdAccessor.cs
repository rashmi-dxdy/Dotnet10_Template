namespace BuildingBlocks.Abstractions.Correlation;

/// <summary>
/// The correlation ID of the operation being processed (the current HTTP request in an API), so outgoing calls
/// and messages can carry it on and one operation can be followed across modules in the logs.
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>Gets the current correlation ID, or <see langword="null"/> outside a request.</summary>
    string? CorrelationId { get; }
}
