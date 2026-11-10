using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Checks once, right after startup, that the service's PostgreSQL database is reachable and logs a clear
/// message either way. It does not stop the service: PostgreSQL may simply start later (e.g. in Docker),
/// EF Core retries transient failures, and <c>/health/ready</c> reports the database as unhealthy meanwhile.
/// </summary>
/// <param name="connectionString">The service's connection string.</param>
/// <param name="timeout">How long to wait for a connection.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PostgresStartupCheck(
    PostgresConnectionString connectionString,
    TimeSpan timeout,
    ILogger<PostgresStartupCheck> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var result = await PostgresConnectivityProbe.ProbeAsync(connectionString, timeout, stoppingToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            LogConnected(logger, connectionString.Name, connectionString.Target);
        }
        else if (!stoppingToken.IsCancellationRequested)
        {
            LogUnavailable(logger, result.Exception, connectionString.Name, connectionString.Target, result.Reason!);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "PostgreSQL {ConnectionName} is reachable at {DatabaseTarget}")]
    private static partial void LogConnected(ILogger logger, string connectionName, string databaseTarget);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "PostgreSQL {ConnectionName} is unavailable at {DatabaseTarget}: {Reason} The service keeps running, "
            + "but database operations will fail and /health/ready reports Unhealthy until PostgreSQL is reachable. "
            + "Check ConnectionStrings:{ConnectionName} (user secrets or environment variable). See docs/database.md")]
    private static partial void LogUnavailable(
        ILogger logger,
        Exception? exception,
        string connectionName,
        string databaseTarget,
        string reason);
}
