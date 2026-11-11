namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ settings, bound from the <c>RabbitMq</c> configuration section. Every value can be overridden with an
/// environment variable, e.g. <c>RabbitMq__Host</c>, <c>RabbitMq__Password</c>. Credentials are never committed.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>Gets or sets the broker host name. Environment variable: <c>RabbitMq__Host</c>.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Gets or sets the AMQP port. Environment variable: <c>RabbitMq__Port</c>.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the user name. Environment variable: <c>RabbitMq__Username</c>.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the password. Environment variable: <c>RabbitMq__Password</c>. Never commit it.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the virtual host. Environment variable: <c>RabbitMq__VirtualHost</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the durable topic exchange every module publishes to and consumes from.</summary>
    public string Exchange { get; set; } = "dotnet10template.events";

    /// <summary>
    /// Gets or sets the connection name shown in the RabbitMQ management UI. Defaults to the host name.
    /// </summary>
    public string? ClientProvidedName { get; set; }

    /// <summary>Gets or sets how long to wait when opening a connection, in seconds.</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>Gets or sets the heartbeat interval used to detect dead connections, in seconds.</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>Gets or sets how long to wait between automatic reconnection attempts after a connection loss.</summary>
    public int NetworkRecoveryIntervalSeconds { get; set; } = 5;

    /// <summary>Gets or sets the first delay between initial connection attempts, in seconds (doubles each time).</summary>
    public int InitialConnectRetryDelaySeconds { get; set; } = 1;

    /// <summary>Gets or sets the longest delay between initial connection attempts, in seconds.</summary>
    public int MaxConnectRetryDelaySeconds { get; set; } = 30;

    /// <summary>Gets or sets how often a failed publish is retried before giving up.</summary>
    public int PublishRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether a message no queue is bound to fails the publish. Off by default:
    /// an event with no subscribers yet is normal.
    /// </summary>
    public bool Mandatory { get; set; }

    /// <summary>Gets or sets how many unacknowledged messages a consumer may hold at once.</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether a message whose handler threw is requeued (retried) rather than
    /// dead-lettered. Requeued messages are still dead-lettered after <see cref="DeliveryLimit"/> attempts.
    /// </summary>
    public bool RequeueOnException { get; set; } = true;

    /// <summary>
    /// Gets or sets how many times a message may be redelivered (quorum queue <c>x-delivery-limit</c>) before it
    /// moves to the dead-letter queue. Changing it for an existing queue requires recreating the queue.
    /// </summary>
    public int DeliveryLimit { get; set; } = 5;

    /// <summary>Gets a description that is safe to log: <c>user@host:port/vhost</c>, never the password.</summary>
    public string Target
        => $"{Username}@{Host}:{Port}{(VirtualHost.StartsWith('/') ? VirtualHost : "/" + VirtualHost)}";

    /// <summary>Lists configuration problems, with the setting and environment variable to fix.</summary>
    /// <returns>The problems; empty when the settings are valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        void Require(bool valid, string setting, string rule)
        {
            if (!valid)
            {
                problems.Add($"{SectionName}:{setting} {rule} (environment variable {SectionName}__{setting})");
            }
        }

        Require(!string.IsNullOrWhiteSpace(Host), nameof(Host), "is required");
        Require(Port is > 0 and <= 65535, nameof(Port), "must be between 1 and 65535");
        Require(!string.IsNullOrWhiteSpace(Username), nameof(Username), "is required");
        Require(!string.IsNullOrEmpty(Password), nameof(Password), "is required");
        Require(!string.IsNullOrWhiteSpace(VirtualHost), nameof(VirtualHost), "is required");
        Require(!string.IsNullOrWhiteSpace(Exchange), nameof(Exchange), "is required");
        Require(ConnectionTimeoutSeconds > 0, nameof(ConnectionTimeoutSeconds), "must be positive");
        Require(HeartbeatSeconds > 0, nameof(HeartbeatSeconds), "must be positive");
        Require(NetworkRecoveryIntervalSeconds > 0, nameof(NetworkRecoveryIntervalSeconds), "must be positive");
        Require(InitialConnectRetryDelaySeconds > 0, nameof(InitialConnectRetryDelaySeconds), "must be positive");
        Require(
            MaxConnectRetryDelaySeconds >= InitialConnectRetryDelaySeconds,
            nameof(MaxConnectRetryDelaySeconds),
            "must be at least InitialConnectRetryDelaySeconds");
        Require(PublishRetryCount >= 0, nameof(PublishRetryCount), "must not be negative");
        Require(PrefetchCount > 0, nameof(PrefetchCount), "must be positive");
        Require(DeliveryLimit > 0, nameof(DeliveryLimit), "must be positive");

        return problems;
    }
}
