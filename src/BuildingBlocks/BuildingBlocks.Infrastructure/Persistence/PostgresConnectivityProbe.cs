using System.Net.Sockets;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Opens (and closes) one PostgreSQL connection to check that the database is reachable, and explains a
/// failure in plain words. Uses ADO.NET directly, without EF Core's retry strategy, so it fails fast.
/// </summary>
internal static class PostgresConnectivityProbe
{
    /// <summary>Tries to connect within <paramref name="timeout"/>.</summary>
    /// <param name="connectionString">The connection string to test.</param>
    /// <param name="timeout">How long to wait for the connection.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>The result; <see cref="ProbeResult.Exception"/> is set when the connection failed.</returns>
    public static async Task<ProbeResult> ProbeAsync(
        PostgresConnectionString connectionString,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString.Value);
            await connection.OpenAsync(timeoutSource.Token).ConfigureAwait(false);
            return ProbeResult.Success;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(
                $"no response within {timeout.TotalSeconds:0} s. Is PostgreSQL running and reachable at that host and port?",
                exception);
        }
        catch (Exception exception) when (exception is NpgsqlException or SocketException or TimeoutException)
        {
            return new ProbeResult(Explain(exception), exception);
        }
    }

    private static string Explain(Exception exception) => exception switch
    {
        PostgresException { SqlState: var state } when IsAuthenticationFailure(state)
            => "authentication failed. Check the Username and Password in the connection string.",
        PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName }
            => "the database does not exist. Create it (the template creates no databases or tables) "
            + "or fix Database=.",
        PostgresException { SqlState: PostgresErrorCodes.TooManyConnections }
            => "the server refused the connection: too many connections.",
        PostgresException postgres => $"the server returned {postgres.SqlState}: {postgres.MessageText}",

        // Any other failure means the server never answered: refused, timed out, DNS, TLS...
        _ => $"cannot reach the server ({Innermost(exception).Message.TrimEnd('.')}). Is PostgreSQL running "
            + "(e.g. `docker compose up -d postgres`) and are Host and Port correct?",
    };

    private static bool IsAuthenticationFailure(string sqlState) =>
        sqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification;

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }
}

/// <summary>The outcome of a connectivity probe.</summary>
/// <param name="Reason">Why the connection failed, in plain words; <see langword="null"/> on success.</param>
/// <param name="Exception">The underlying exception; <see langword="null"/> on success.</param>
internal sealed record ProbeResult(string? Reason, Exception? Exception)
{
    public static readonly ProbeResult Success = new(null, null);

    public bool IsSuccess => Exception is null;
}
