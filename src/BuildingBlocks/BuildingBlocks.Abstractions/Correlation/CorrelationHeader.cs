namespace BuildingBlocks.Abstractions.Correlation;

/// <summary>The HTTP header that carries the correlation ID between clients, the host and modules.</summary>
public static class CorrelationHeader
{
    /// <summary>The header name.</summary>
    public const string Name = "X-Correlation-ID";
}
