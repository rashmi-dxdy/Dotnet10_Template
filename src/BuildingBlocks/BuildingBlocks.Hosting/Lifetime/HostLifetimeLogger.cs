using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Hosting.Lifetime;

/// <summary>
/// Logs graceful shutdown: when it starts (with the configured drain timeout) and when it has finished
/// (with the total uptime). Startup itself is logged by <c>Microsoft.Hosting.Lifetime</c>.
/// </summary>
/// <param name="lifetime">The application lifetime.</param>
/// <param name="hostOptions">Host options, for the shutdown timeout.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class HostLifetimeLogger(
    IHostApplicationLifetime lifetime,
    IOptions<HostOptions> hostOptions,
    ILogger<HostLifetimeLogger> logger) : IHostedService, IDisposable
{
    private readonly List<CancellationTokenRegistration> _registrations = [];
    private long _startedTimestamp;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startedTimestamp = Stopwatch.GetTimestamp();

        // Not disposed in StopAsync: ApplicationStopped fires after every hosted service has stopped.
        _registrations.Add(lifetime.ApplicationStopping.Register(OnStopping));
        _registrations.Add(lifetime.ApplicationStopped.Register(OnStopped));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose() => _registrations.ForEach(r => r.Dispose());

    private void OnStopping() => LogStopping(logger, hostOptions.Value.ShutdownTimeout);

    private void OnStopped()
    {
        var uptime = Stopwatch.GetElapsedTime(_startedTimestamp);
        LogStopped(logger, uptime);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Graceful shutdown started; waiting up to {ShutdownTimeout} for in-flight work to finish")]
    private static partial void LogStopping(ILogger logger, TimeSpan shutdownTimeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful shutdown completed after {Uptime} uptime")]
    private static partial void LogStopped(ILogger logger, TimeSpan uptime);
}
