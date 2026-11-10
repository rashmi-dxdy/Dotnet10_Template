using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Consumes one queue on its own channel of the shared connection and passes each message to
/// <typeparamref name="THandler"/> in a new DI scope. Manual acknowledgements only:
/// <list type="bullet">
///   <item><see cref="MessageHandlingResult.Ack"/> → <c>basic.ack</c>;</item>
///   <item><see cref="MessageHandlingResult.Retry"/> → <c>basic.nack</c> with requeue (dead-lettered after the delivery limit);</item>
///   <item><see cref="MessageHandlingResult.Reject"/> or invalid JSON → <c>basic.nack</c> without requeue (dead-lettered);</item>
///   <item>handler exception → requeue or dead-letter, per <c>RabbitMq:RequeueOnException</c>.</item>
/// </list>
/// Graceful shutdown: stops receiving, lets the in-flight message finish (until the host shutdown timeout), then
/// closes the channel. Unacknowledged messages are redelivered by the broker.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="THandler">The handler type.</typeparam>
internal sealed class RabbitMqConsumer<TMessage, THandler> : BackgroundService
    where TMessage : class
    where THandler : IMessageHandler<TMessage>
{
    private static readonly string MessageType = typeof(TMessage).Name;

    private readonly RabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqSubscription _subscription;
    private readonly ILogger<RabbitMqConsumer<TMessage, THandler>> _logger;
    private readonly string _topics;

    // Held while a message is being handled, so shutdown can wait for it.
    private readonly SemaphoreSlim _inFlight = new(1, 1);

    // Cancelled only when the host's shutdown timeout is up: in-flight handlers are not interrupted earlier.
    private readonly CancellationTokenSource _hardStop = new();

    public RabbitMqConsumer(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        RabbitMqSubscription subscription,
        ILogger<RabbitMqConsumer<TMessage, THandler>> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _subscription = subscription;
        _logger = logger;
        _topics = string.Join(", ", subscription.Topics);
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
        _inFlight.Dispose();
        _hardStop.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IChannel? channel = null;
        string? consumerTag = null;
        try
        {
            (channel, consumerTag) = await StartConsumingWithRetryAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            await StopConsumingAsync(channel, consumerTag).ConfigureAwait(false);
        }
    }

    private async Task<(IChannel Channel, string ConsumerTag)> StartConsumingWithRetryAsync(
        CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(_options.InitialConnectRetryDelaySeconds);
        var maxDelay = TimeSpan.FromSeconds(_options.MaxConnectRetryDelaySeconds);

        while (true)
        {
            IChannel? channel = null;
            try
            {
                var connection = await _connection.GetConnectionAsync(stoppingToken).ConfigureAwait(false);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
                await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken)
                    .ConfigureAwait(false);
                await RabbitMqTopology.DeclareQueueAsync(channel, _options, _subscription, stoppingToken)
                    .ConfigureAwait(false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery);
                var consumerTag = await channel.BasicConsumeAsync(
                    _subscription.Queue,
                    autoAck: false,
                    consumer,
                    stoppingToken)
                    .ConfigureAwait(false);

                RabbitMqLog.ConsumerStarted(_logger, MessageType, _subscription.Queue, _topics, _options.PrefetchCount);
                return (channel, consumerTag);
            }
            catch (Exception exception) when (exception is RabbitMqUnavailableException or AlreadyClosedException
                or OperationInterruptedException)
            {
                // The connection lifetime service logs broker unavailability; this only notes the consumer retry.
                RabbitMqLog.ConsumerStartFailed(_logger, exception, _subscription.Queue, delay);
                if (channel is not null)
                {
                    await channel.DisposeAsync().ConfigureAwait(false);
                }

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        await _inFlight.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var properties = delivery.BasicProperties;
            var messageId = properties.MessageId ?? $"(delivery {delivery.DeliveryTag})";
            using var scope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["MessageId"] = messageId,
                ["CorrelationId"] = properties.CorrelationId,
                ["Queue"] = _subscription.Queue,
            });

            var result = await ProcessAsync(delivery, messageId).ConfigureAwait(false);
            await SettleAsync(channel, delivery.DeliveryTag, messageId, result).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.Release();
        }
    }

    private async Task<MessageHandlingResult> ProcessAsync(BasicDeliverEventArgs delivery, string messageId)
    {
        TMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, RabbitMqPublisher.JsonOptions);
        }
        catch (JsonException exception)
        {
            RabbitMqLog.InvalidMessage(_logger, exception, messageId, _subscription.Queue, MessageType);
            return MessageHandlingResult.Reject;
        }

        if (message is null)
        {
            RabbitMqLog.InvalidMessage(_logger, null, messageId, _subscription.Queue, MessageType);
            return MessageHandlingResult.Reject;
        }

        var deliveryCount = DeliveryCount(delivery.BasicProperties);
        var context = new MessageContext<TMessage>(
            message,
            messageId,
            delivery.BasicProperties.CorrelationId,
            delivery.RoutingKey,
            delivery.Redelivered,
            deliveryCount);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            return await handler.HandleAsync(context, _hardStop.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || _hardStop.IsCancellationRequested)
        {
            var outcome = _options.RequeueOnException
                ? $"requeueing it (dead-lettered after {_options.DeliveryLimit} redeliveries)"
                : "dead-lettering it";
            RabbitMqLog.HandlerFailed(_logger, exception, MessageType, messageId, deliveryCount, outcome);
            return _options.RequeueOnException ? MessageHandlingResult.Retry : MessageHandlingResult.Reject;
        }
    }

    private async Task SettleAsync(IChannel channel, ulong deliveryTag, string messageId, MessageHandlingResult result)
    {
        try
        {
            switch (result)
            {
                case MessageHandlingResult.Ack:
                    await channel.BasicAckAsync(deliveryTag, multiple: false, CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                case MessageHandlingResult.Retry:
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                default:
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
            }

            RabbitMqLog.Settled(_logger, messageId, _subscription.Queue, result);
        }
        catch (Exception exception) when (exception is AlreadyClosedException or OperationInterruptedException)
        {
            // The channel went away (connection loss or shutdown); the broker redelivers unacknowledged messages.
            RabbitMqLog.SettleFailed(_logger, exception, messageId, result);
        }
    }

    private async Task StopConsumingAsync(IChannel? channel, string? consumerTag)
    {
        if (channel is null)
        {
            return;
        }

        try
        {
            if (consumerTag is not null && channel.IsOpen)
            {
                await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            // Wait for the message being handled, unless the shutdown timeout is up.
            await _inFlight.WaitAsync(_hardStop.Token).ConfigureAwait(false);
            _inFlight.Release();

            if (channel.IsOpen)
            {
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or AlreadyClosedException
            or OperationInterruptedException)
        {
            // Shutdown timeout reached or connection already gone: unacknowledged messages are redelivered.
        }
        finally
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            RabbitMqLog.ConsumerStopped(_logger, _subscription.Queue);
        }
    }

    // Quorum queues count failed deliveries in the x-delivery-count header (absent on the first delivery).
    private static int DeliveryCount(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue("x-delivery-count", out var value) == true
            ? value switch
            {
                long count => (int)count,
                int count => count,
                byte[] text when int.TryParse(
                    Encoding.UTF8.GetString(text),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var count) => count,
                _ => 0,
            }
            : 0;
}
