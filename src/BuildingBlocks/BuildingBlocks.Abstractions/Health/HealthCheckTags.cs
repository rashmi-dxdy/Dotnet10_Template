namespace BuildingBlocks.Abstractions.Health;

/// <summary>
/// Tags that decide which health endpoint runs a health check.
/// </summary>
public static class HealthCheckTags
{
    /// <summary>
    /// The check must pass before the application can receive traffic (e.g. its database is reachable).
    /// Run by <c>/health/ready</c>; never by <c>/health/live</c>.
    /// </summary>
    public const string Ready = "ready";
}
