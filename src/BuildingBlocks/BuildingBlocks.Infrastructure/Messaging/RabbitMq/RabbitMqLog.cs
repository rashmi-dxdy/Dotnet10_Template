using BuildingBlocks.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// All RabbitMQ log messages. Connection details are logged as <c>user@host:port/vhost</c>, never the password.
/// </summary>
internal static partial class RabbitMqLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ {BrokerTarget}")]
    public static partial void Connected(ILogger logger, string brokerTarget);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "RabbitMQ {BrokerTarget} is unavailable: {Reason} The service keeps running and retries every "
            + "{RetryDelay} at most; publishing and consuming wait until it is reachable, and /health/ready reports "
            + "Unhealthy meanwhile. Check RabbitMq:Host, Port, Username, Password and VirtualHost (user secrets or "
            + "RabbitMq__* environment variables). See docs/messaging.md")]
    public static partial void Unavailable(
        ILogger logger,
        Exception exception,
        string brokerTarget,
        string reason,
        TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "RabbitMQ {BrokerTarget} still unavailable after {Attempt} attempts: {Reason} Next attempt in {RetryDelay}")]
    public static partial void StillUnavailable(
        ILogger logger,
        string brokerTarget,
        int attempt,
        string reason,
        TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "RabbitMQ connection to {BrokerTarget} lost: {Reason}. Reconnecting automatically every {RecoveryInterval}")]
    public static partial void ConnectionLost(
        ILogger logger,
        string brokerTarget,
        string reason,
        TimeSpan recoveryInterval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ reconnection to {BrokerTarget} failed; retrying")]
    public static partial void RecoveryFailed(ILogger logger, Exception exception, string brokerTarget);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnected to RabbitMQ {BrokerTarget}")]
    public static partial void Recovered(ILogger logger, string brokerTarget);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed RabbitMQ connection to {BrokerTarget}")]
    public static partial void Closed(ILogger logger, string brokerTarget);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the RabbitMQ connection to {BrokerTarget} failed")]
    public static partial void CloseFailed(ILogger logger, Exception exception, string brokerTarget);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published {MessageType} {MessageId} to {Exchange}/{Topic}")]
    public static partial void Published(
        ILogger logger,
        string messageType,
        string messageId,
        string exchange,
        string topic);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Publishing {MessageType} to {Topic} failed (attempt {Attempt} of {MaxAttempts}); retrying in {RetryDelay}")]
    public static partial void PublishRetry(
        ILogger logger,
        Exception exception,
        string messageType,
        string topic,
        int attempt,
        int maxAttempts,
        TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Consuming {MessageType} from queue {Queue} (topics {Topics}, prefetch {PrefetchCount})")]
    public static partial void ConsumerStarted(
        ILogger logger,
        string messageType,
        string queue,
        string topics,
        ushort prefetchCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped consuming from queue {Queue}")]
    public static partial void ConsumerStopped(ILogger logger, string queue);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not start consuming from queue {Queue}; retrying in {RetryDelay}")]
    public static partial void ConsumerStartFailed(
        ILogger logger,
        Exception exception,
        string queue,
        TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Message {MessageId} in queue {Queue} is not valid {MessageType} JSON; dead-lettering it")]
    public static partial void InvalidMessage(
        ILogger logger,
        Exception? exception,
        string messageId,
        string queue,
        string messageType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handler for {MessageType} failed on message {MessageId} (delivery {DeliveryCount}); {Outcome}")]
    public static partial void HandlerFailed(
        ILogger logger,
        Exception exception,
        string messageType,
        string messageId,
        int deliveryCount,
        string outcome);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message {MessageId} from queue {Queue}: {Result}")]
    public static partial void Settled(ILogger logger, string messageId, string queue, MessageHandlingResult result);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not settle message {MessageId} ({Result}); the broker will redeliver it")]
    public static partial void SettleFailed(
        ILogger logger,
        Exception exception,
        string messageId,
        MessageHandlingResult result);
}
