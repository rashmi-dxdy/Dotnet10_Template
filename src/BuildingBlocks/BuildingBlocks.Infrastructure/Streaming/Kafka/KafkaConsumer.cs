using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Abstractions.Streaming;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Consumes one topic as a member of a consumer group and passes each message to <typeparamref name="THandler"/>
/// in a new DI scope, one message at a time per consumer (so per-partition order is kept).
/// <list type="bullet">
///   <item><b>Offsets:</b> stored only after the handler succeeded or the message was dead-lettered, committed in
///   the background, on partition revocation and on shutdown (at-least-once).</item>
///   <item><b>Errors:</b> a failing handler is retried <c>Consumer:MaxRetryAttempts</c> times with exponential
///   backoff; then the message goes to <c>&lt;topic&gt;.&lt;group&gt;.dlt</c> with diagnostic headers. Messages
///   that are not valid JSON go there directly.</item>
///   <item><b>Shutdown:</b> stops reading, lets the in-flight message finish (until the host shutdown timeout),
///   commits offsets and leaves the group, so its partitions are reassigned immediately.</item>
/// </list>
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="THandler">The handler type.</typeparam>
internal sealed class KafkaConsumer<TMessage, THandler> : BackgroundService
    where TMessage : class
    where THandler : IStreamMessageHandler<TMessage>
{
    private const int MaxReasonLength = 500;
    private readonly string _messageType = typeof(TMessage).Name;
    private readonly TimeSpan _deadLetterRetryDelay = TimeSpan.FromSeconds(5);

    private readonly KafkaOptions _options;
    private readonly KafkaProducer _producer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaConsumer<TMessage, THandler>> _logger;
    private readonly string _topic;
    private readonly string _groupId;
    private readonly string _deadLetterTopic;

    // Cancelled only when the host's shutdown timeout is up: the in-flight handler is not interrupted earlier.
    private readonly CancellationTokenSource _hardStop = new();

    public KafkaConsumer(
        IOptions<KafkaOptions> options,
        KafkaProducer producer,
        IServiceScopeFactory scopeFactory,
        KafkaSubscription subscription,
        ILogger<KafkaConsumer<TMessage, THandler>> logger)
    {
        _options = options.Value;
        _producer = producer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _topic = subscription.Topic;
        var defaultGroupId = KafkaClientConfiguration.GroupId(_options);
        _groupId = subscription.ResolveGroupId(defaultGroupId);
        _deadLetterTopic = subscription.DeadLetterTopic(defaultGroupId);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await using var registration = cancellationToken.Register(_hardStop.Cancel).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _hardStop.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    // Consume() blocks, so the loop runs on its own task instead of the host's startup path.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), CancellationToken.None);

    private static string Truncate(string text) => text.Length <= MaxReasonLength ? text : text[..MaxReasonLength];

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var events = new KafkaClientEvents(_logger, $"consumer {_topic}");
        using var consumer = new ConsumerBuilder<string?, byte[]>(KafkaClientConfiguration.Consumer(_options, _groupId))
            .SetErrorHandler((_, error) => events.OnError(error))
            .SetLogHandler((_, message) => events.OnLog(message))
            .SetPartitionsAssignedHandler((_, partitions) => OnPartitionsAssigned(partitions))
            .SetPartitionsRevokedHandler((_, partitions) => OnPartitionsRevoked(partitions))
            .Build();

        consumer.Subscribe(_topic);
        KafkaLog.ConsumerStarted(_logger, _messageType, _topic, _groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string?, byte[]>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException exception) when (!exception.Error.IsFatal)
                {
                    // E.g. the topic does not exist yet (and broker auto-creation is off). Keep polling.
                    KafkaLog.ConsumeFailed(_logger, _topic, exception.Error.Reason, _deadLetterRetryDelay);
                    await Task.Delay(_deadLetterRetryDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (result?.Message is not null)
                {
                    await ProcessAsync(consumer, result, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            try
            {
                // Commits stored offsets and leaves the group, so partitions are reassigned without waiting for
                // the session timeout.
                consumer.Close();
                KafkaLog.ConsumerStopped(_logger, _topic, _groupId);
            }
            catch (KafkaException exception)
            {
                KafkaLog.ConsumerCloseFailed(_logger, exception, _topic);
            }
        }
    }

    private void OnPartitionsAssigned(List<TopicPartition> partitions)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var assigned = string.Join(", ", partitions);
            KafkaLog.PartitionsAssigned(_logger, _groupId, assigned);
        }
    }

    private void OnPartitionsRevoked(List<TopicPartitionOffset> partitions)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var revoked = string.Join(", ", partitions.Select(p => p.TopicPartition));
            KafkaLog.PartitionsRevoked(_logger, _groupId, revoked);
        }
    }

    private async Task ProcessAsync(
        IConsumer<string?, byte[]> consumer,
        ConsumeResult<string?, byte[]> result,
        CancellationToken stoppingToken)
    {
        var headers = result.Message.Headers;
        var messageId = KafkaHeaders.Get(headers, KafkaHeaders.MessageId)
            ?? $"{result.Topic}[{result.Partition.Value}]@{result.Offset.Value}";
        using var scope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["MessageId"] = messageId,
            ["CorrelationId"] = KafkaHeaders.Get(headers, KafkaHeaders.CorrelationId),
            ["Topic"] = result.Topic,
            ["Partition"] = result.Partition.Value,
            ["Offset"] = result.Offset.Value,
        });

        TMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(result.Message.Value, KafkaProducer.JsonOptions);
        }
        catch (JsonException exception)
        {
            await DeadLetterAndStoreAsync(consumer, result, $"not valid {_messageType} JSON", exception, stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        if (message is null)
        {
            await DeadLetterAndStoreAsync(consumer, result, $"empty {_messageType} message", null, stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        await HandleWithRetriesAsync(consumer, result, message, messageId, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleWithRetriesAsync(
        IConsumer<string?, byte[]> consumer,
        ConsumeResult<string?, byte[]> result,
        TMessage message,
        string messageId,
        CancellationToken stoppingToken)
    {
        var maxAttempts = _options.Consumer.MaxRetryAttempts + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await HandleAsync(result, message, messageId, attempt).ConfigureAwait(false);
                StoreOffset(consumer, result);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                || _hardStop.IsCancellationRequested)
            {
                if (attempt == maxAttempts)
                {
                    await DeadLetterAndStoreAsync(consumer, result, exception.Message, exception, stoppingToken)
                        .ConfigureAwait(false);
                    return;
                }

                var delay = TimeSpan.FromMilliseconds(_options.Consumer.RetryBackoffMs * Math.Pow(2, attempt - 1));
                KafkaLog.HandlerRetry(
                    _logger,
                    exception,
                    _messageType,
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    attempt,
                    maxAttempts,
                    delay);

                // Stopping during a backoff: leave the offset uncommitted, so the message is processed after restart.
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(
        ConsumeResult<string?, byte[]> result,
        TMessage message,
        string messageId,
        int attempt)
    {
        var context = new StreamMessageContext<TMessage>(
            message,
            messageId,
            KafkaHeaders.Get(result.Message.Headers, KafkaHeaders.CorrelationId),
            result.Message.Key,
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            new DateTimeOffset(result.Message.Timestamp.UtcDateTime, TimeSpan.Zero),
            attempt);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await handler.HandleAsync(context, _hardStop.Token).ConfigureAwait(false);
    }

    private async Task DeadLetterAndStoreAsync(
        IConsumer<string?, byte[]> consumer,
        ConsumeResult<string?, byte[]> result,
        string reason,
        Exception? exception,
        CancellationToken stoppingToken)
    {
        var headers = new Headers();
        foreach (var header in result.Message.Headers ?? [])
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        KafkaHeaders.Set(headers, KafkaHeaders.DeadLetterOriginalTopic, result.Topic);
        KafkaHeaders.Set(
            headers,
            KafkaHeaders.DeadLetterOriginalPartition,
            result.Partition.Value.ToString(CultureInfo.InvariantCulture));
        KafkaHeaders.Set(
            headers,
            KafkaHeaders.DeadLetterOriginalOffset,
            result.Offset.Value.ToString(CultureInfo.InvariantCulture));
        KafkaHeaders.Set(headers, KafkaHeaders.DeadLetterConsumerGroup, _groupId);
        KafkaHeaders.Set(headers, KafkaHeaders.DeadLetterReason, Truncate(reason));
        KafkaHeaders.Set(headers, KafkaHeaders.DeadLetterExceptionType, exception?.GetType().FullName);

        var deadLetter = new Message<string?, byte[]>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = headers,
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _producer.ProduceRawAsync(_deadLetterTopic, deadLetter, _hardStop.Token).ConfigureAwait(false);
                KafkaLog.DeadLettered(
                    _logger,
                    exception,
                    _messageType,
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    Truncate(reason),
                    _deadLetterTopic);
                StoreOffset(consumer, result);
                return;
            }
            catch (ProduceException<string?, byte[]> produceException)
            {
                // Never skip a message silently: keep the offset and try again (the partition waits).
                KafkaLog.DeadLetterFailed(
                    _logger,
                    produceException,
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    _deadLetterTopic,
                    _deadLetterRetryDelay);
                await Task.Delay(_deadLetterRetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static void StoreOffset(IConsumer<string?, byte[]> consumer, ConsumeResult<string?, byte[]> result)
    {
        try
        {
            consumer.StoreOffset(result);
        }
        catch (KafkaException exception) when (exception.Error.Code == ErrorCode.Local_State)
        {
            // The partition was revoked during processing: its new owner processes the message again.
        }
    }
}
