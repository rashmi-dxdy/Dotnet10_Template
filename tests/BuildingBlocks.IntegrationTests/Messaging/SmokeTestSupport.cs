using System.Globalization;
using System.Threading.Channels;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Infrastructure.Messaging.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.IntegrationTests.Messaging;

/// <summary>A generic, non-business message used only by the smoke tests.</summary>
/// <param name="EventId">The event ID.</param>
/// <param name="OccurredOn">When it was created.</param>
/// <param name="Text">A payload.</param>
internal sealed record SmokeTestMessage(Guid EventId, DateTimeOffset OccurredOn, string Text) : IIntegrationEvent
{
    public static SmokeTestMessage Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "ping");
}

/// <summary>Records every delivery and decides the outcome from the attempt number.</summary>
/// <param name="behavior">Returns the result for the given attempt (1-based), or throws.</param>
internal sealed class SmokeTestRecorder(Func<int, MessageHandlingResult> behavior)
{
    private readonly Channel<MessageContext<SmokeTestMessage>> _received = Channel
        .CreateUnbounded<MessageContext<SmokeTestMessage>>();
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public MessageHandlingResult Record(MessageContext<SmokeTestMessage> context)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        _received.Writer.TryWrite(context);
        return behavior(attempt);
    }

    public async Task<MessageContext<SmokeTestMessage>> NextAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await _received.Reader.ReadAsync(cts.Token);
    }
}

/// <summary>The smoke test handler: delegates to the recorder.</summary>
/// <param name="recorder">The shared recorder.</param>
internal sealed class SmokeTestHandler(SmokeTestRecorder recorder) : IMessageHandler<SmokeTestMessage>
{
    public Task<MessageHandlingResult> HandleAsync(
        MessageContext<SmokeTestMessage> context,
        CancellationToken cancellationToken) =>
        Task.FromResult(recorder.Record(context));
}

/// <summary>
/// A direct broker connection for test setup and assertions (queue counts, cleanup), independent of the
/// adapter under test. Skips the test when the broker is not configured or not reachable.
/// </summary>
internal sealed class RabbitMqTestBroker : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqTestBroker(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqTestBroker> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        var username = Environment.GetEnvironmentVariable("RabbitMq__Username");
        var password = Environment.GetEnvironmentVariable("RabbitMq__Password");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Assert.Skip("RabbitMQ is not configured: set RabbitMq__Host, RabbitMq__Port, RabbitMq__Username, "
                + "RabbitMq__Password and RabbitMq__VirtualHost to run the smoke tests (see docs/messaging.md).");
        }

        var configuredPort = Environment.GetEnvironmentVariable("RabbitMq__Port");
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMq__Host") ?? "localhost",
            Port = int.TryParse(configuredPort, CultureInfo.InvariantCulture, out var port) ? port : 5672,
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMq__VirtualHost") ?? "/",
            UserName = username,
            Password = password,
        };

        try
        {
            var connection = await factory.CreateConnectionAsync(cancellationToken);
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            return new RabbitMqTestBroker(connection, channel);
        }
        catch (BrokerUnreachableException exception)
        {
            Assert.Skip($"RabbitMQ at {factory.HostName}:{factory.Port} is not reachable: {exception.Message}");
            throw;
        }
    }

    public async Task WaitForQueueAsync(string queue, TimeSpan timeout) =>
        await WaitUntilAsync(
            async () => await TryGetMessageCountAsync(queue) is not null,
            timeout,
            $"queue {queue} to exist");

    public async Task WaitForMessageCountAsync(string queue, uint expected, TimeSpan timeout) =>
        await WaitUntilAsync(
            async () => await TryGetMessageCountAsync(queue) == expected,
            timeout,
            $"{expected} message(s) in {queue}");

    public async Task<bool> TryWaitForMessageCountAsync(string queue, uint expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await TryGetMessageCountAsync(queue) != expected)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(200);
        }

        return true;
    }

    public async Task<uint?> MessageCountAsync(string queue) => await TryGetMessageCountAsync(queue);

    public async Task DeleteAsync(string exchange, string queue)
    {
        await using var channel = await _connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
        await channel.QueueDeleteAsync(queue + RabbitMqTopology.DeadLetterSuffix, ifUnused: false, ifEmpty: false);
        await channel.ExchangeDeleteAsync(exchange, ifUnused: false);
        await channel.ExchangeDeleteAsync(RabbitMqTopology.DeadLetterExchange(exchange), ifUnused: false);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out after {timeout.TotalSeconds:0} s waiting for {what}.");
            await Task.Delay(200);
        }
    }

    private async Task<uint?> TryGetMessageCountAsync(string queue)
    {
        try
        {
            // A passive declare on a missing queue closes the channel, so use a throwaway one.
            await using var channel = await _connection.CreateChannelAsync();
            var result = await channel.QueueDeclarePassiveAsync(queue);
            return result.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            return null;
        }
    }
}
