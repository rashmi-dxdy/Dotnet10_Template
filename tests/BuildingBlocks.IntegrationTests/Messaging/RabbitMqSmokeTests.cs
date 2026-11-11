using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.IntegrationTests.Messaging;

/// <summary>
/// Infrastructure smoke tests for the RabbitMQ adapter against a real broker: publish with confirms, consume,
/// ack, nack with and without requeue, delivery limit and dead-lettering, health check and graceful shutdown.
/// Uses a generic test message only. Configure the broker with the same environment variables as the services
/// (<c>RabbitMq__Host</c>, <c>RabbitMq__Port</c>, <c>RabbitMq__Username</c>, <c>RabbitMq__Password</c>,
/// <c>RabbitMq__VirtualHost</c>); without them every test is skipped.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqSmokeTests : IAsyncLifetime
{
    private const string Topic = "smoke-test.ping";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    // Unique names per test, so runs never interfere and cleanup removes everything this test created.
    private readonly string _exchange = $"smoke-test.{Guid.NewGuid():N}";
    private readonly string _queue = $"smoke-test.{Guid.NewGuid():N}";
    private RabbitMqTestBroker? _broker;
    private IHost? _host;

    public async ValueTask InitializeAsync() =>
        _broker = await RabbitMqTestBroker.ConnectOrSkipAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }

        if (_broker is not null)
        {
            await _broker.DeleteAsync(_exchange, _queue);
            await _broker.DisposeAsync();
        }
    }

    [Fact]
    public async Task PublishedMessage_IsConfirmed_Consumed_AndAcknowledged()
    {
        var recorder = new SmokeTestRecorder(_ => MessageHandlingResult.Ack);
        await StartHostAsync(recorder);
        var message = SmokeTestMessage.Create();

        await Publisher.PublishAsync(
            Topic,
            message,
            correlationId: "smoke-correlation",
            TestContext.Current.CancellationToken);

        var received = await recorder.NextAsync(Timeout);
        Assert.Equal(message, received.Message);
        Assert.Equal(message.EventId.ToString(), received.MessageId);
        Assert.Equal("smoke-correlation", received.CorrelationId);
        Assert.Equal(Topic, received.Topic);
        Assert.False(received.Redelivered);
        await _broker!.WaitForMessageCountAsync(_queue, 0, Timeout);
    }

    [Fact]
    public async Task Retry_RequeuesTheMessage_AndItIsRedelivered()
    {
        var recorder = new SmokeTestRecorder(
            attempt => attempt == 1 ? MessageHandlingResult.Retry : MessageHandlingResult.Ack);
        await StartHostAsync(recorder);

        await Publisher.PublishAsync(
            Topic,
            SmokeTestMessage.Create(),
            cancellationToken: TestContext.Current.CancellationToken);

        var first = await recorder.NextAsync(Timeout);
        var second = await recorder.NextAsync(Timeout);
        Assert.False(first.Redelivered);
        Assert.True(second.Redelivered);
        Assert.Equal(1, second.DeliveryCount);
        await _broker!.WaitForMessageCountAsync(_queue, 0, Timeout);
    }

    [Fact]
    public async Task Reject_DeadLettersTheMessage_WithoutRequeue()
    {
        var recorder = new SmokeTestRecorder(_ => MessageHandlingResult.Reject);
        await StartHostAsync(recorder);

        await Publisher.PublishAsync(
            Topic,
            SmokeTestMessage.Create(),
            cancellationToken: TestContext.Current.CancellationToken);

        await recorder.NextAsync(Timeout);
        await _broker!.WaitForMessageCountAsync(_queue + RabbitMqTopology.DeadLetterSuffix, 1, Timeout);
        Assert.Equal(1, recorder.Attempts);
    }

    [Fact]
    public async Task FailingHandler_IsRetriedUpToTheDeliveryLimit_ThenDeadLettered()
    {
        var recorder = new SmokeTestRecorder(_ => throw new InvalidOperationException("Simulated handler failure"));
        await StartHostAsync(recorder, deliveryLimit: 2);

        await Publisher.PublishAsync(
            Topic,
            SmokeTestMessage.Create(),
            cancellationToken: TestContext.Current.CancellationToken);

        // First delivery + 2 redeliveries, then the broker dead-letters it.
        var deadLettered = await _broker!.TryWaitForMessageCountAsync(
            _queue + RabbitMqTopology.DeadLetterSuffix,
            1,
            Timeout);
        Assert.True(
            deadLettered,
            $"Not dead-lettered after {Timeout.TotalSeconds:0} s: handler ran {recorder.Attempts} time(s), "
            + $"main queue holds {await _broker.MessageCountAsync(_queue)} message(s).");
        Assert.Equal(3, recorder.Attempts);
    }

    [Fact]
    public async Task UnroutableMessage_WithMandatory_FailsThePublish()
    {
        await StartHostAsync(new SmokeTestRecorder(_ => MessageHandlingResult.Ack), mandatory: true);

        var exception = await Assert.ThrowsAsync<MessagePublishException>(() => Publisher.PublishAsync(
            "smoke-test.nobody-listens",
            SmokeTestMessage.Create(),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("smoke-test.nobody-listens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthCheck_IsHealthy_WhileConnected()
    {
        await StartHostAsync(new SmokeTestRecorder(_ => MessageHandlingResult.Ack));

        var report = await Service<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Entries["rabbitmq"].Status);
    }

    private IMessagePublisher Publisher => Service<IMessagePublisher>();

    private T Service<T>()
        where T : notnull =>
        (T)_host!.Services.GetService(typeof(T))!;

    private async Task StartHostAsync(SmokeTestRecorder recorder, int deliveryLimit = 5, bool mandatory = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RabbitMq:Exchange"] = _exchange,
            ["RabbitMq:DeliveryLimit"] = deliveryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Mandatory"] = mandatory.ToString(),
        });

        builder.Services.AddSingleton(recorder);
        builder.Services.AddRabbitMqMessaging(builder.Configuration);
        builder.Services.AddRabbitMqConsumer<SmokeTestMessage, SmokeTestHandler>(_queue, Topic);

        _host = builder.Build();
        await _host.StartAsync(TestContext.Current.CancellationToken);

        // The consumer declares its queue in the background; publish only once it is bound.
        await _broker!.WaitForQueueAsync(_queue, Timeout);
    }
}
