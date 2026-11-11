using Confluent.Kafka;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Kafka settings, bound from the <c>Kafka</c> configuration section, independent of the <c>RabbitMq</c> section.
/// Every value can be overridden with an environment variable, e.g. <c>Kafka__BootstrapServers</c>,
/// <c>Kafka__Consumer__GroupId</c>, <c>Kafka__SaslPassword</c>. Credentials are never committed.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Gets or sets the comma-separated <c>host:port</c> list of brokers to bootstrap from.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Gets or sets which IP version is used to reach brokers: <c>Any</c> (default), <c>V4</c> or <c>V6</c>. Use <c>V4</c>
    /// for Kafka in Docker on Windows: <c>localhost</c> resolves to IPv6 <c>::1</c> first, where the container port is
    /// usually not published, which makes connections fail intermittently.
    /// </summary>
    public BrokerAddressFamily BrokerAddressFamily { get; set; } = BrokerAddressFamily.Any;

    /// <summary>Gets or sets the client ID shown in broker logs and metrics. Defaults to the service name.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets how clients talk to brokers: <c>Plaintext</c> (local development), <c>Ssl</c>,
    /// <c>SaslPlaintext</c> or <c>SaslSsl</c> (production).</summary>
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;

    /// <summary>Gets or sets the SASL mechanism for <c>Sasl*</c> protocols: <c>Plain</c>, <c>ScramSha256</c>, <c>ScramSha512</c>.</summary>
    public SaslMechanism SaslMechanism { get; set; } = SaslMechanism.Plain;

    /// <summary>Gets or sets the SASL user name. Environment variable: <c>Kafka__SaslUsername</c>.</summary>
    public string SaslUsername { get; set; } = string.Empty;

    /// <summary>Gets or sets the SASL password. Environment variable: <c>Kafka__SaslPassword</c>. Never commit it.</summary>
    public string SaslPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets the CA certificate file used to verify brokers with <c>Ssl</c>/<c>SaslSsl</c>.</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>Gets or sets the timeout for connectivity checks and topic administration, in seconds.</summary>
    public int AdminTimeoutSeconds { get; set; } = 5;

    /// <summary>Gets the producer settings.</summary>
    public KafkaProducerOptions Producer { get; init; } = new();

    /// <summary>Gets the consumer settings.</summary>
    public KafkaConsumerOptions Consumer { get; init; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the service creates missing topics (<see cref="Topics"/> and its
    /// consumers' dead-letter topics) at startup. On in Development; in production topics are created by
    /// infrastructure tooling with the right partitions, replication and retention.
    /// </summary>
    public bool ProvisionTopics { get; set; }

    /// <summary>Gets or sets the partitions and replication factor for provisioned topics without explicit values.</summary>
    public KafkaTopicDefaults TopicDefaults { get; set; } = new();

    /// <summary>Gets the topics this service owns (produces to), created when <see cref="ProvisionTopics"/> is on.</summary>
    public IList<KafkaTopicOptions> Topics { get; init; } = [];

    /// <summary>Lists configuration problems, with the setting and environment variable to fix.</summary>
    /// <returns>The problems; empty when the settings are valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        void Require(bool valid, string setting, string rule)
        {
            if (!valid)
            {
                var variable = $"{SectionName}__{setting.Replace(":", "__", StringComparison.Ordinal)}";
                problems.Add($"{SectionName}:{setting} {rule} (environment variable {variable})");
            }
        }

        Require(!string.IsNullOrWhiteSpace(BootstrapServers), nameof(BootstrapServers), "is required");
        Require(AdminTimeoutSeconds > 0, nameof(AdminTimeoutSeconds), "must be positive");

        if (SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl
            && SaslMechanism is not (SaslMechanism.OAuthBearer or SaslMechanism.Gssapi))
        {
            Require(
                !string.IsNullOrWhiteSpace(SaslUsername),
                nameof(SaslUsername),
                $"is required for {SecurityProtocol}");
            Require(!string.IsNullOrEmpty(SaslPassword), nameof(SaslPassword), $"is required for {SecurityProtocol}");
        }

        Require(Producer.MessageTimeoutMs > 0, "Producer:MessageTimeoutMs", "must be positive");
        Require(Producer.LingerMs >= 0, "Producer:LingerMs", "must not be negative");
        Require(Consumer.MaxRetryAttempts >= 0, "Consumer:MaxRetryAttempts", "must not be negative");
        Require(Consumer.RetryBackoffMs > 0, "Consumer:RetryBackoffMs", "must be positive");
        Require(Consumer.SessionTimeoutMs > 0, "Consumer:SessionTimeoutMs", "must be positive");
        Require(
            Consumer.MaxPollIntervalMs > Consumer.SessionTimeoutMs,
            "Consumer:MaxPollIntervalMs",
            "must exceed SessionTimeoutMs");
        Require(TopicDefaults.Partitions > 0, "TopicDefaults:Partitions", "must be positive");
        Require(TopicDefaults.ReplicationFactor > 0, "TopicDefaults:ReplicationFactor", "must be positive");

        for (var i = 0; i < Topics.Count; i++)
        {
            Require(!string.IsNullOrWhiteSpace(Topics[i].Name), $"Topics:{i}:Name", "is required");
        }

        return problems;
    }
}

/// <summary>Producer reliability settings.</summary>
public sealed class KafkaProducerOptions
{
    /// <summary>
    /// Gets or sets how many replicas must acknowledge a write: <c>All</c> (default, no data loss while one
    /// in-sync replica survives), <c>Leader</c> or <c>None</c>.
    /// </summary>
    public Acks Acks { get; set; } = Acks.All;

    /// <summary>
    /// Gets or sets a value indicating whether the idempotent producer is on: client retries never create
    /// duplicates or reorder messages within a partition. Requires <c>Acks=All</c>.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>Gets or sets how long the client keeps retrying a message before failing it, in milliseconds.</summary>
    public int MessageTimeoutMs { get; set; } = 30_000;

    /// <summary>Gets or sets how long to wait to batch messages before sending, in milliseconds.</summary>
    public int LingerMs { get; set; } = 5;

    /// <summary>Gets or sets the compression codec: <c>None</c>, <c>Gzip</c>, <c>Snappy</c>, <c>Lz4</c> or <c>Zstd</c>.</summary>
    public CompressionType CompressionType { get; set; } = CompressionType.Lz4;
}

/// <summary>Consumer group, offset and error-handling settings.</summary>
public sealed class KafkaConsumerOptions
{
    /// <summary>
    /// Gets or sets the consumer group. All instances of a service share it, so partitions are divided between
    /// them and each message is handled once per group. Environment variable: <c>Kafka__Consumer__GroupId</c>.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets where a NEW group starts reading: <c>Earliest</c> (default, the whole retained history) or
    /// <c>Latest</c> (only new messages). Ignored once the group has committed offsets.
    /// </summary>
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;

    /// <summary>Gets or sets how often processed offsets are committed in the background, in milliseconds.</summary>
    public int AutoCommitIntervalMs { get; set; } = 5_000;

    /// <summary>Gets or sets how many times a failed message is retried in-process before dead-lettering.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets the first retry delay in milliseconds (doubles on each attempt).</summary>
    public int RetryBackoffMs { get; set; } = 1_000;

    /// <summary>Gets or sets after how long without heartbeats the broker removes a consumer from the group.</summary>
    public int SessionTimeoutMs { get; set; } = 45_000;

    /// <summary>
    /// Gets or sets the longest time one message (including retries) may take before the consumer is considered
    /// stuck and its partitions are reassigned, in milliseconds.
    /// </summary>
    public int MaxPollIntervalMs { get; set; } = 300_000;
}

/// <summary>Defaults for provisioned topics.</summary>
public sealed class KafkaTopicDefaults
{
    /// <summary>Gets or sets the number of partitions: the maximum consumer parallelism within one group.</summary>
    public int Partitions { get; set; } = 3;

    /// <summary>Gets or sets the replication factor: <c>1</c> for a single local broker, <c>3</c> in production.</summary>
    public short ReplicationFactor { get; set; } = 1;
}

/// <summary>A topic this service owns.</summary>
public sealed class KafkaTopicOptions
{
    /// <summary>Gets or sets the topic name, e.g. <c>orders.order-events</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the partitions; <see langword="null"/> uses <see cref="KafkaTopicDefaults.Partitions"/>.</summary>
    public int? Partitions { get; set; }

    /// <summary>Gets or sets the replication factor; <see langword="null"/> uses the default.</summary>
    public short? ReplicationFactor { get; set; }

    /// <summary>Gets or sets how long messages are kept, in milliseconds; <see langword="null"/> uses the broker default.</summary>
    public long? RetentionMs { get; set; }
}
