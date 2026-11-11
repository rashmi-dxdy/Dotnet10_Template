using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// The process's single, long-lived RabbitMQ connection, shared by the publisher and all consumers. Never one
/// connection per message: channels are opened on it as needed. Once established, RabbitMQ.Client's automatic
/// recovery reconnects it and restores channels, queues, bindings and consumers after a network failure.
/// </summary>
internal sealed class RabbitMqConnection : IAsyncDisposable, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ClientProvidedName ?? Assembly.GetEntryAssembly()?.GetName().Name,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds),
            RequestedHeartbeat = TimeSpan.FromSeconds(_options.HeartbeatSeconds),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds),
            ConsumerDispatchConcurrency = 1,
        };
    }

    /// <summary>Gets the broker description that is safe to log.</summary>
    public string Target => _options.Target;

    /// <summary>Gets a value indicating whether the connection is currently open (not missing or recovering).</summary>
    public bool IsConnected => _connection is { IsOpen: true };

    /// <summary>Gets why the last connection attempt failed, in plain words; <see langword="null"/> if it did not.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Returns the shared connection, opening it on first use. While the broker is down after a connection loss,
    /// the returned connection is recovering and operations on it fail until it is back.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The shared connection.</returns>
    /// <exception cref="RabbitMqUnavailableException">The broker cannot be reached.</exception>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } existing)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { } created)
            {
                return created;
            }

            try
            {
                var connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                Subscribe(connection);
                _connection = connection;
                LastError = null;
                RabbitMqLog.Connected(_logger, Target);
                return connection;
            }
            catch (Exception exception) when (exception is BrokerUnreachableException or OperationInterruptedException
                or SocketException or TimeoutException)
            {
                LastError = Explain(exception);
                throw new RabbitMqUnavailableException($"RabbitMQ {Target} is unavailable: {LastError}", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the connection gracefully (part of graceful shutdown).</summary>
    /// <param name="cancellationToken">Cancelled when the shutdown timeout is up.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_connection is not { IsOpen: true } connection)
        {
            return;
        }

        try
        {
            await connection.CloseAsync(cancellationToken).ConfigureAwait(false);
            RabbitMqLog.Closed(_logger, Target);
        }
        catch (Exception exception) when (exception is OperationCanceledException or AlreadyClosedException
            or OperationInterruptedException)
        {
            RabbitMqLog.CloseFailed(_logger, exception, Target);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection?.Dispose();
        _gate.Dispose();
    }

    private static string Explain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AuthenticationFailureException:
                    return "authentication failed. Check RabbitMq:Username and RabbitMq:Password.";
                case OperationInterruptedException { ShutdownReason.ReplyCode: 530 }:
                    return "access to the virtual host was refused: it does not exist or the user has no permissions "
                        + "on it. Check RabbitMq:VirtualHost.";
                case SocketException socket:
                    return $"cannot reach the broker ({socket.SocketErrorCode}). Is RabbitMQ running "
                        + "(e.g. `docker compose up -d rabbitmq`) and are Host and Port correct?";
            }
        }

        var innermost = exception;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        return $"cannot reach the broker ({innermost.Message.TrimEnd('.')}). "
            + "Is RabbitMQ running and are Host and Port correct?";
    }

    private void Subscribe(IConnection connection)
    {
        connection.ConnectionShutdownAsync += (_, args) =>
        {
            if (args.Initiator != ShutdownInitiator.Application)
            {
                RabbitMqLog.ConnectionLost(_logger, Target, args.ReplyText, _factory.NetworkRecoveryInterval);
            }

            return Task.CompletedTask;
        };
        connection.ConnectionRecoveryErrorAsync += (_, args) =>
        {
            RabbitMqLog.RecoveryFailed(_logger, args.Exception, Target);
            return Task.CompletedTask;
        };
        connection.RecoverySucceededAsync += (_, _) =>
        {
            RabbitMqLog.Recovered(_logger, Target);
            return Task.CompletedTask;
        };
    }
}

/// <summary>
/// The broker could not be reached; the message says why, in plain words. Used inside the RabbitMQ adapter only:
/// publishers see <c>MessagePublishException</c>.
/// </summary>
public sealed class RabbitMqUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RabbitMqUnavailableException"/> class.</summary>
    public RabbitMqUnavailableException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RabbitMqUnavailableException"/> class.</summary>
    /// <param name="message">Why the broker is unavailable.</param>
    public RabbitMqUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RabbitMqUnavailableException"/> class.</summary>
    /// <param name="message">Why the broker is unavailable.</param>
    /// <param name="innerException">The broker client's exception.</param>
    public RabbitMqUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
