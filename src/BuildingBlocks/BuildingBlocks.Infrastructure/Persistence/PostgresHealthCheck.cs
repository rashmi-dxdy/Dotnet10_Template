using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Reports whether a module's PostgreSQL database accepts connections. Tagged
/// <c>ready</c>, so it runs on <c>/health/ready</c> only.
/// </summary>
/// <param name="connectionString">The module's connection string.</param>
/// <param name="timeout">How long to wait for a connection.</param>
internal sealed class PostgresHealthCheck(PostgresConnectionString connectionString, TimeSpan timeout) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await PostgresConnectivityProbe.ProbeAsync(connectionString, timeout, cancellationToken)
            .ConfigureAwait(false);

        // The description goes to health check logs/publishers, so it uses the safe Target, never the value.
        return result.IsSuccess
            ? HealthCheckResult.Healthy($"PostgreSQL {connectionString.Target} is reachable.")
            : new HealthCheckResult(
                context?.Registration.FailureStatus ?? HealthStatus.Unhealthy,
                $"PostgreSQL {connectionString.Target} is unavailable: {result.Reason}",
                result.Exception);
    }
}
