using RabbitMQ.Client;

namespace BuildingBlocks.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Declares the broker topology. All declarations are idempotent and durable:
/// <list type="bullet">
///   <item>one durable <c>topic</c> exchange for all services (<c>RabbitMq:Exchange</c>);</item>
///   <item>per consumer queue: a durable quorum queue bound to its topics, with a delivery limit;</item>
///   <item>a dead-letter exchange <c>&lt;exchange&gt;.dlx</c> and a dead-letter queue <c>&lt;queue&gt;.dlq</c> that
///   receives rejected messages and messages that exceeded the delivery limit.</item>
/// </list>
/// </summary>
internal static class RabbitMqTopology
{
    public const string DeadLetterSuffix = ".dlq";

    public static string DeadLetterExchange(string exchange) => exchange + ".dlx";

    public static Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken cancellationToken) =>
        channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

    public static async Task DeclareQueueAsync(
        IChannel channel,
        RabbitMqOptions options,
        RabbitMqSubscription subscription,
        CancellationToken cancellationToken)
    {
        var deadLetterExchange = DeadLetterExchange(options.Exchange);
        var deadLetterQueue = subscription.Queue + DeadLetterSuffix;

        await DeclareExchangeAsync(channel, options.Exchange, cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(
                deadLetterExchange,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.QueueDeclareAsync(
                deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["x-queue-type"] = "quorum" },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await channel.QueueBindAsync(
                deadLetterQueue,
                deadLetterExchange,
                routingKey: subscription.Queue,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.QueueDeclareAsync(
                subscription.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["x-queue-type"] = "quorum",
                    ["x-dead-letter-exchange"] = deadLetterExchange,
                    ["x-dead-letter-routing-key"] = subscription.Queue,
                    ["x-delivery-limit"] = options.DeliveryLimit,
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var topic in subscription.Topics)
        {
            await channel.QueueBindAsync(
                subscription.Queue,
                options.Exchange,
                topic,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>A consumer's queue and the topics bound to it.</summary>
/// <param name="Queue">The queue name, e.g. <c>notifications.order-placed</c>.</param>
/// <param name="Topics">The topics routed to the queue, e.g. <c>orders.order-placed</c>; wildcards allowed.</param>
internal sealed record RabbitMqSubscription(string Queue, IReadOnlyList<string> Topics);
