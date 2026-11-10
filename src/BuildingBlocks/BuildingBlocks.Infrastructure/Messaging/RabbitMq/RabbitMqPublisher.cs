using System.Text.Json;
using BuildingBlocks.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// <see cref="IMessagePublisher"/> for RabbitMQ. Uses one long-lived channel on the shared connection with
/// publisher confirms: <see cref="PublishAsync"/> completes only after the broker confirmed the message.
/// Messages are persistent (delivery mode 2) and published to the durable topic exchange. Transient failures
/// are retried with backoff.
/// </summary>
/// <param name="connection">The shared connection.</param>
/// <param name="options">The RabbitMQ settings.</param>
/// <param name="logger">The logger.</param>
internal sealed class RabbitMqPublisher(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IMessagePublisher, IAsyncDisposable, IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A channel must not be used for concurrent publishes; publishing is serialized on this one channel.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    /// <inheritdoc />
    public async Task PublishAsync<TMessage>(
        string topic,
        TMessage message,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        var messageType = typeof(TMessage).Name;
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = (message as IIntegrationEvent)?.EventId.ToString() ?? Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            Type = messageType,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var maxAttempts = settings.PublishRetryCount + 1;

        var attempt = 1;
        while (true)
        {
            try
            {
                await PublishOnceAsync(settings, topic, properties, body, cancellationToken).ConfigureAwait(false);
                RabbitMqLog.Published(logger, messageType, properties.MessageId, settings.Exchange, topic);
                return;
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                RabbitMqLog.PublishRetry(logger, exception, messageType, topic, attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                attempt++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new MessagePublishException(
                    $"Publishing {messageType} {properties.MessageId} to '{settings.Exchange}/{topic}' failed after "
                    + $"{attempt} attempt(s): {exception.Message}",
                    exception);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_channel is { } channel)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel?.Dispose();
        _gate.Dispose();
    }

    private static bool IsTransient(Exception exception) =>
        exception is RabbitMqUnavailableException or AlreadyClosedException or BrokerUnreachableException
            or OperationInterruptedException
        || (exception is PublishException && exception is not PublishReturnException);

    private async Task PublishOnceAsync(
        RabbitMqOptions settings,
        string topic,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await GetChannelAsync(settings, cancellationToken).ConfigureAwait(false);

            // With publisher confirmation tracking, this awaits the broker's confirm and throws if it nacks
            // (PublishException) or, with Mandatory, returns the message as unroutable (PublishReturnException).
            await channel.BasicPublishAsync(
                    settings.Exchange,
                    topic,
                    settings.Mandatory,
                    properties,
                    body,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }

        if (_channel is { } closed)
        {
            await closed.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        var shared = await connection.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var channel = await shared.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken)
            .ConfigureAwait(false);
        await RabbitMqTopology.DeclareExchangeAsync(channel, settings.Exchange, cancellationToken)
            .ConfigureAwait(false);

        _channel = channel;
        return channel;
    }
}
