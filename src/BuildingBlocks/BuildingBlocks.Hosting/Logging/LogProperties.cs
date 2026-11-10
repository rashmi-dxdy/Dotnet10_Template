namespace BuildingBlocks.Hosting.Logging;

/// <summary>
/// Names of the log properties every service writes. Use these constants instead of string literals so
/// queries in the log backend work the same for every service.
/// </summary>
public static class LogProperties
{
    /// <summary>The service name, e.g. <c>Catalog.Api</c>.</summary>
    public const string Application = "Application";

    /// <summary>The hosting environment, e.g. <c>Development</c> or <c>Production</c>.</summary>
    public const string Environment = "Environment";

    /// <summary>
    /// The correlation ID of the current request or message. HTTP APIs set it from the
    /// <c>X-Correlation-ID</c> header. Message consumers should push it with
    /// <c>LogContext.PushProperty(LogProperties.CorrelationId, id)</c>.
    /// </summary>
    public const string CorrelationId = "CorrelationId";
}
