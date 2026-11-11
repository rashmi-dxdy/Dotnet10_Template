namespace BuildingBlocks.Hosting.Logging;

/// <summary>
/// Names of the log properties every module writes. Use these constants instead of string literals so
/// queries in the log backend work the same for every module.
/// </summary>
public static class LogProperties
{
    /// <summary>The application (host) name, e.g. <c>Host.Api</c>.</summary>
    public const string Application = "Application";

    /// <summary>
    /// The module that handled the request, e.g. <c>catalog</c>. Set for every request to a module endpoint, so the
    /// logs of one module can be filtered while all modules share one process.
    /// </summary>
    public const string Module = "Module";

    /// <summary>The hosting environment, e.g. <c>Development</c> or <c>Production</c>.</summary>
    public const string Environment = "Environment";

    /// <summary>
    /// The correlation ID of the current request or message. HTTP APIs set it from the
    /// <c>X-Correlation-ID</c> header. Message consumers should push it with
    /// <c>LogContext.PushProperty(LogProperties.CorrelationId, id)</c>.
    /// </summary>
    public const string CorrelationId = "CorrelationId";
}
