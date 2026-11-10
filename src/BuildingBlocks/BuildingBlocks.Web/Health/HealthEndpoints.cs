using BuildingBlocks.Abstractions.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web.Health;

/// <summary>
/// The health endpoints every HTTP API exposes. Responses contain only the status (<c>Healthy</c>,
/// <c>Degraded</c>, <c>Unhealthy</c>), never check details, hosts or error messages.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Liveness: the process is running. Runs no checks, so a database outage never restarts it.</summary>
    public const string LivePath = "/health/live";

    /// <summary>Readiness: the service can do its work. Runs every check tagged <c>ready</c> (e.g. PostgreSQL).</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Maps <see cref="LivePath"/> and <see cref="ReadyPath"/>.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapServiceHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(
            ReadyPath,
            new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Ready) });

        return endpoints;
    }
}
