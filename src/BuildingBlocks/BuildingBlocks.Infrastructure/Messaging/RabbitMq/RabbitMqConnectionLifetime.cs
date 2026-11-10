using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Owns the connection lifecycle. On startup it connects in the background, retrying with exponential backoff
/// until the broker is reachable, so the service starts even if RabbitMQ is not up yet. On shutdown it closes the
/// connection gracefully. It is registered before every consumer, so the host stops it LAST: consumers finish
/// their in-flight messages and close their channels first.
/// </summary>
/// <param name="connection">The shared connection.</param>
/// <param name="options">The RabbitMQ settings.</param>
/// <param name="logger">The logger.</param>
internal sealed class RabbitMqConnectionLifetime(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnectionLifetime> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _connectLoop = Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Run off the startup path: the host must not wait for the broker.
        _connectLoop = Task.Run(() => ConnectWithRetryAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _connectLoop.ConfigureAwait(false);
        await connection.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(options.Value.InitialConnectRetryDelaySeconds);
        var maxDelay = TimeSpan.FromSeconds(options.Value.MaxConnectRetryDelaySeconds);

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await connection.GetConnectionAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (RabbitMqUnavailableException exception)
            {
                if (attempt == 1)
                {
                    RabbitMqLog.Unavailable(logger, exception, connection.Target, connection.LastError!, maxDelay);
                }
                else
                {
                    RabbitMqLog.StillUnavailable(logger, connection.Target, attempt, connection.LastError!, delay);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
        }
    }
}
