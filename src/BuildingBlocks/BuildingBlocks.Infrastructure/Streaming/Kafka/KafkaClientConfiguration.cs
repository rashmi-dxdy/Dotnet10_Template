using System.Reflection;
using Confluent.Kafka;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Builds the Confluent.Kafka client configurations from <see cref="KafkaOptions"/>. Connection and security
/// settings are shared; producer and consumer settings are applied on top.
/// </summary>
internal static class KafkaClientConfiguration
{
    public static string ClientId(KafkaOptions options) =>
        string.IsNullOrWhiteSpace(options.ClientId)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "dotnet10template"
            : options.ClientId;

    public static string GroupId(KafkaOptions options) =>
        string.IsNullOrWhiteSpace(options.Consumer.GroupId) ? ClientId(options) : options.Consumer.GroupId;

    /// <summary>A description that is safe to log: brokers, protocol and user, never the password.</summary>
    public static string Target(KafkaOptions options) =>
        options.SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl
            ? $"{options.BootstrapServers} ({options.SecurityProtocol}, {options.SaslMechanism}, "
                + $"user {options.SaslUsername})"
            : $"{options.BootstrapServers} ({options.SecurityProtocol})";

    public static ProducerConfig Producer(KafkaOptions options)
    {
        var config = new ProducerConfig(Common(options))
        {
            Acks = options.Producer.Acks,
            EnableIdempotence = options.Producer.EnableIdempotence,
            MessageTimeoutMs = options.Producer.MessageTimeoutMs,
            LingerMs = options.Producer.LingerMs,
            CompressionType = options.Producer.CompressionType,
        };

        return config;
    }

    public static ConsumerConfig Consumer(KafkaOptions options, string groupId) =>
        new(Common(options))
        {
            GroupId = groupId,
            AutoOffsetReset = options.Consumer.AutoOffsetReset,

            // At-least-once: offsets are STORED manually after a message was handled (or dead-lettered), and the
            // client commits stored offsets in the background, on partition revocation and on Close().
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            AutoCommitIntervalMs = options.Consumer.AutoCommitIntervalMs,
            SessionTimeoutMs = options.Consumer.SessionTimeoutMs,
            MaxPollIntervalMs = options.Consumer.MaxPollIntervalMs,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            EnablePartitionEof = false,
        };

    public static AdminClientConfig Admin(KafkaOptions options) => new(Common(options));

    private static ClientConfig Common(KafkaOptions options)
    {
        var config = new ClientConfig
        {
            BootstrapServers = options.BootstrapServers,
            BrokerAddressFamily = options.BrokerAddressFamily,
            ClientId = ClientId(options),
            SecurityProtocol = options.SecurityProtocol,
            SocketTimeoutMs = options.AdminTimeoutSeconds * 1000 * 2,
        };

        if (options.SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl)
        {
            config.SaslMechanism = options.SaslMechanism;
            config.SaslUsername = options.SaslUsername;
            config.SaslPassword = options.SaslPassword;
        }

        if (!string.IsNullOrWhiteSpace(options.SslCaLocation))
        {
            config.SslCaLocation = options.SslCaLocation;
        }

        return config;
    }
}
