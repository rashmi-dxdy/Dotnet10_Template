using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Reports whether the shared RabbitMQ connection is open. Tagged <c>ready</c>, so it runs on
/// <c>/health/ready</c> only. It never opens connections itself: the connection lifetime service does that.
/// </summary>
/// <param name="connection">The shared connection.</param>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = connection.IsConnected
            ? HealthCheckResult.Healthy($"RabbitMQ {connection.Target} is connected.")
            : new HealthCheckResult(
                context?.Registration.FailureStatus ?? HealthStatus.Unhealthy,
                $"RabbitMQ {connection.Target} is not connected: {connection.LastError ?? "connecting or recovering."}");

        return Task.FromResult(result);
    }
}
