using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Reports whether the Kafka cluster answers a metadata request. Tagged <c>ready</c>, so it runs on
/// <c>/health/ready</c> only. Answers within <c>Kafka:AdminTimeoutSeconds</c>.
/// </summary>
/// <param name="admin">The admin client.</param>
internal sealed class KafkaHealthCheck(KafkaAdmin admin) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var (brokers, error) = await admin.CheckAsync(cancellationToken).ConfigureAwait(false);

        return error is null
            ? HealthCheckResult.Healthy($"Kafka {admin.Target} is reachable ({brokers} broker(s)).")
            : new HealthCheckResult(
                context?.Registration.FailureStatus ?? HealthStatus.Unhealthy,
                $"Kafka {admin.Target} is unavailable: {error}");
    }
}
