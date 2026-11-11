using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// All Kafka log messages. Broker details are logged via <see cref="KafkaClientConfiguration.Target"/>, never
/// the SASL password.
/// </summary>
internal static partial class KafkaLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Kafka {KafkaTarget}: {BrokerCount} broker(s)")]
    public static partial void Connected(ILogger logger, string kafkaTarget, int brokerCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Kafka {KafkaTarget} is unavailable: {Reason}. The service keeps running and the clients keep "
            + "reconnecting; producing waits up to Kafka:Producer:MessageTimeoutMs and /health/ready reports Unhealthy "
            + "meanwhile. Check Kafka:BootstrapServers and the security settings (Kafka__* environment variables). "
            + "See docs/streaming.md")]
    public static partial void Unavailable(ILogger logger, string kafkaTarget, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka client {ClientName} error: {Reason} ({ErrorCode})")]
    public static partial void ClientError(ILogger logger, string clientName, string reason, ErrorCode errorCode);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Kafka client {ClientName} fatal error: {Reason} ({ErrorCode})")]
    public static partial void ClientFatalError(ILogger logger, string clientName, string reason, ErrorCode errorCode);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Created Kafka topic {Topic} ({Partitions} partitions, replication {ReplicationFactor})")]
    public static partial void TopicCreated(ILogger logger, string topic, int partitions, short replicationFactor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create Kafka topic {Topic}: {Reason}")]
    public static partial void TopicCreationFailed(ILogger logger, string topic, string reason);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Produced {MessageType} {MessageId} to {Topic}[{Partition}]@{Offset}")]
    public static partial void Produced(
        ILogger logger,
        string messageType,
        string messageId,
        string topic,
        int partition,
        long offset);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Flushed Kafka producer before shutdown ({Pending} message(s) still pending)")]
    public static partial void Flushed(ILogger logger, int pending);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Consuming {MessageType} from Kafka topic {Topic} as consumer group {GroupId}")]
    public static partial void ConsumerStarted(ILogger logger, string messageType, string topic, string groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer group {GroupId} assigned {Partitions}")]
    public static partial void PartitionsAssigned(ILogger logger, string groupId, string partitions);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Consumer group {GroupId} revoked {Partitions}; stored offsets committed")]
    public static partial void PartitionsRevoked(ILogger logger, string groupId, string partitions);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Stopped consuming Kafka topic {Topic}; offsets committed and group {GroupId} left")]
    public static partial void ConsumerStopped(ILogger logger, string topic, string groupId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the Kafka consumer of {Topic} failed")]
    public static partial void ConsumerCloseFailed(ILogger logger, Exception exception, string topic);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Reading from Kafka topic {Topic} failed: {Reason}; retrying in {RetryDelay}")]
    public static partial void ConsumeFailed(ILogger logger, string topic, string reason, TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Handler for {MessageType} failed on {Topic}[{Partition}]@{Offset} (attempt {Attempt} of {MaxAttempts}); retrying in {RetryDelay}")]
    public static partial void HandlerRetry(
        ILogger logger,
        Exception exception,
        string messageType,
        string topic,
        int partition,
        long offset,
        int attempt,
        int maxAttempts,
        TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{MessageType} at {Topic}[{Partition}]@{Offset} failed: {Reason}. Sent to dead-letter topic {DeadLetterTopic}")]
    public static partial void DeadLettered(
        ILogger logger,
        Exception? exception,
        string messageType,
        string topic,
        int partition,
        long offset,
        string reason,
        string deadLetterTopic);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Could not dead-letter {Topic}[{Partition}]@{Offset} to {DeadLetterTopic}; the message will be "
            + "processed again in {RetryDelay}. The partition is blocked until this succeeds")]
    public static partial void DeadLetterFailed(
        ILogger logger,
        Exception exception,
        string topic,
        int partition,
        long offset,
        string deadLetterTopic,
        TimeSpan retryDelay);
}
