# Messaging: RabbitMQ

Modules exchange integration events through RabbitMQ (RabbitMQ.Client 7.2, Apache-2.0 / MPL-2.0). Application
code depends only on two technology-agnostic interfaces. The RabbitMQ adapter lives in Infrastructure. All
modules run in one process today ([modules.md](modules.md)), yet they still talk through the broker exactly as
they will once a module is extracted.

## 1. Layers

| Layer | What | RabbitMQ.Client? |
|---|---|---|
| `BuildingBlocks.Abstractions/Messaging` | `IMessagePublisher`, `IMessageHandler<T>`, `MessageContext<T>`, `MessageHandlingResult`, `MessagePublishException`, `IIntegrationEvent` | No (pure C#) |
| `Contracts/IntegrationEvents` | The event records modules exchange (none yet) | No |
| Domain, Application | Publish through `IMessagePublisher`; handle through `IMessageHandler<T>` | **No (enforced)** |
| `BuildingBlocks.Infrastructure/Messaging/RabbitMq` | The adapter: connection, publisher, consumers, topology, health check | Yes |
| `<Module>.Infrastructure` | `Add<Module>Messaging()`: registers the adapter (once per process) and the module's consumers | Yes |

`Architecture.Tests` fails if any project other than an Infrastructure project references RabbitMQ.Client or
Confluent.Kafka, or if Domain/Application/Abstractions code contains `using RabbitMQ` **(enforced)**.

## 2. Configuration

Section `RabbitMq`, overridden by `RabbitMq__*` environment variables. Credentials are never committed: the
settings tests reject a committed `Password` value **(enforced)**.

| Setting | Environment variable | Default |
|---|---|---|
| `Host` | `RabbitMq__Host` | `localhost` |
| `Port` | `RabbitMq__Port` | `5672` |
| `VirtualHost` | `RabbitMq__VirtualHost` | `/` |
| `Username` | `RabbitMq__Username` | (required) |
| `Password` | `RabbitMq__Password` | (required) |
| `Exchange` | `RabbitMq__Exchange` | `dotnet10template.events` |
| `PrefetchCount` | | `10` unacknowledged messages per consumer |
| `PublishRetryCount` | | `3` |
| `Mandatory` | | `false` (an event with no subscribers is not an error) |
| `RequeueOnException` | | `true` |
| `DeliveryLimit` | | `5` redeliveries before dead-lettering |
| `NetworkRecoveryIntervalSeconds` | | `5` |
| `InitialConnectRetryDelaySeconds` / `MaxConnectRetryDelaySeconds` | | `1` / `30` |
| `ConnectionTimeoutSeconds` / `HeartbeatSeconds` | | `10` / `30` |

Missing or invalid settings stop the host at startup with a list of what to fix and the environment variable
for each.

Development (from `src/Host/Host.Api`, or right-click `Host.Api` > *Manage User Secrets* in
Visual Studio / *Tools > .NET User Secrets* in Rider):

```bash
cd src/Host/Host.Api
dotnet user-secrets set "RabbitMq:Username" "app"
dotnet user-secrets set "RabbitMq:Password" "<your local password>"
```

Docker Compose (values from the git-ignored `.env`):

```yaml
x-rabbitmq: &rabbitmq
  RabbitMq__Host: rabbitmq
  RabbitMq__Port: "5672"
  RabbitMq__VirtualHost: ${RABBITMQ_VHOST:-/}
  RabbitMq__Username: ${RABBITMQ_USER}
  RabbitMq__Password: ${RABBITMQ_PASSWORD}

app:            # Host.Api
  environment:
    <<: *rabbitmq
```

> **Git Bash on Windows:** `export RabbitMq__VirtualHost=/` is rewritten to a Windows path
> (`C:/Program Files/Git/`). Leave it unset to use the default `/`, or set `MSYS_NO_PATHCONV=1`. PowerShell,
> cmd and Docker are not affected.

## 3. Topology

All declared idempotently by the adapter. Nothing to create by hand.

```text
                 topic exchange: dotnet10template.events (durable)
publisher ──► routing key "orders.order-placed" ──► queue "notifications.order-placed" ──► consumer
                                                      (durable quorum queue, x-delivery-limit)
                                                            │ reject / delivery limit exceeded
                                                            ▼
                          direct exchange dotnet10template.events.dlx ──► "notifications.order-placed.dlq"
```

| Name | Convention | Example |
|---|---|---|
| Topic (routing key) | `<publishing-module>.<event>` | `orders.order-placed` |
| Queue | `<consuming-module>.<purpose>` (`QueuePrefix` in each module) | `notifications.order-placed` |
| Dead-letter queue | `<queue>.dlq` | `notifications.order-placed.dlq` |

Queues are **quorum queues** (replicated, durable, and the RabbitMQ 4 default for durable data). Queue arguments
cannot change after creation: changing `DeliveryLimit` for an existing queue means deleting and recreating it.

## 4. Publishing

```csharp
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

await publisher.PublishAsync("orders.order-placed", new OrderPlaced(...), correlationId, cancellationToken);
```

- **Persistent** messages (delivery mode 2) on a **durable** exchange and durable queues survive a broker restart.
- **Publisher confirms**: `PublishAsync` completes only after the broker confirmed the message, and throws
  `MessagePublishException` if it is nacked (or, with `Mandatory`, unroutable).
- Transient failures (connection lost, broker nack) are retried `PublishRetryCount` times with backoff.
- JSON (System.Text.Json, camelCase). `MessageId` = the event's `EventId`. `CorrelationId` is passed along.
- One long-lived publishing channel. Publishes are serialized on it, which is safe and simple. Measure before
  adding a channel pool.

**At-least-once:** a retry after a lost confirm can publish a duplicate, so consumers must be idempotent
(deduplicate by `MessageId`). For "save to the database **and** publish" atomically, add an outbox later.

## 5. Consuming

```csharp
// Infrastructure/Messaging/OrderPlacedHandler.cs
internal sealed class OrderPlacedHandler(ISender sender) : IMessageHandler<OrderPlaced>
{
    public async Task<MessageHandlingResult> HandleAsync(MessageContext<OrderPlaced> context, CancellationToken ct)
    {
        await sender.Send(new SendOrderConfirmation(context.Message.OrderId), ct);
        return MessageHandlingResult.Ack;
    }
}

// DependencyInjection/MessagingServiceCollectionExtensions.cs, in AddNotificationsMessaging
services.AddRabbitMqConsumer<OrderPlaced, OrderPlacedHandler>(QueuePrefix + "order-placed", "orders.order-placed");
```

Each consumer gets its own channel on the shared connection, a prefetch limit, and a new DI scope per message.
**Manual acknowledgements only:**

| Handler outcome | Broker action |
|---|---|
| `MessageHandlingResult.Ack` | `basic.ack`: removed from the queue |
| `MessageHandlingResult.Retry` | `basic.nack` + requeue: redelivered (`Redelivered = true`, `DeliveryCount` + 1) |
| `MessageHandlingResult.Reject` | `basic.nack` without requeue: moved to `<queue>.dlq` |
| Exception | Requeue if `RequeueOnException` (default), else dead-letter. Logged with the message ID |
| Body is not valid JSON for the type | Dead-lettered immediately (retrying cannot fix it) |
| Redelivered more than `DeliveryLimit` times | The broker moves it to `<queue>.dlq`, so a poison message cannot loop forever |

Log entries written while handling carry `MessageId`, `CorrelationId` and `Queue`.

## 6. Connection lifecycle, reconnection and shutdown

- **One connection per process**, shared by the publisher and every module's consumers, named after the host
  in the management UI. Each module still has its own queues, so an extracted module keeps them unchanged. Channels are opened on it; there is never a connection per message.
- **Startup:** the connection is opened in the background. The host starts even if RabbitMQ is down and keeps
  retrying with exponential backoff (1 s, 2 s, 4 s … max 30 s), logging the reason once as an error and then as
  warnings:

  ```text
  [ERR] RabbitMQ app@localhost:5672/ is unavailable: cannot reach the broker (ConnectionRefused). Is RabbitMQ
        running (e.g. `docker compose up -d rabbitmq`) and are Host and Port correct? The service keeps running ...
  [ERR] RabbitMQ app@localhost:5672/ is unavailable: authentication failed. Check RabbitMq:Username and RabbitMq:Password. ...
  ```

- **Connection loss:** RabbitMQ.Client's automatic recovery reconnects every `NetworkRecoveryIntervalSeconds` and
  restores channels, queues, bindings and consumers. Publishing retries meanwhile:

  ```text
  [WRN] RabbitMQ connection to app@localhost:5672/ lost: CONNECTION_FORCED ... Reconnecting automatically every 00:00:05
  [INF] Reconnected to RabbitMQ app@localhost:5672/
  ```

- **Graceful shutdown** (Ctrl+C / SIGTERM): consumers stop receiving, finish the in-flight message (until
  `HostOptions:ShutdownTimeout`) and close their channels. Then the connection is closed. Unacknowledged messages
  go back to the queue. The connection is registered before the consumers, so the host stops it last.
- Logs only ever show `user@host:port/vhost`, never the password.

## 7. Health checks

`rabbitmq` (tag `ready`) is Healthy while the connection is open, and Unhealthy while connecting or recovering.
It runs on `/health/ready`, next to PostgreSQL. It never opens connections itself.

## 8. Smoke tests

`tests/BuildingBlocks.IntegrationTests/Messaging/RabbitMqSmokeTests.cs` runs the adapter against a **real**
broker with a generic test message. It covers publish with confirms, consume, ack, retry with redelivery, reject to
the dead-letter queue, retries up to the delivery limit then dead-letter, a mandatory unroutable publish failing,
and the health check. Each test uses its own exchange and queues and deletes them afterwards.

Without broker settings the tests are **skipped**. To run them, start a broker and set the variables:

```bash
docker run -d --name rabbitmq-dev --user rabbitmq -p 127.0.0.1:5672:5672 -p 127.0.0.1:15672:15672 \
  -e RABBITMQ_DEFAULT_USER=app -e RABBITMQ_DEFAULT_PASS=<password> rabbitmq:4.1-management
```

```powershell
$env:RabbitMq__Username = 'app'; $env:RabbitMq__Password = '<password>'
dotnet test --project tests/BuildingBlocks.IntegrationTests
```

`--user rabbitmq` avoids an `.erlang.cookie: eacces` start-up crash of the official image seen with Docker
Desktop on Windows. The management UI is at <http://localhost:15672>.

## 9. Adding messaging to a module

1. Define the event in `src/Contracts/IntegrationEvents` (a record implementing `IIntegrationEvent`).
2. Publisher: inject `IMessagePublisher` in the Application layer and publish with topic
   `<your-module>.<event>`.
3. Consumer: write an `IMessageHandler<TEvent>` in the consuming module's `Infrastructure/Messaging` that sends
   an Application command, and register it with `AddRabbitMqConsumer` in `Add<Module>Messaging`. Make it
   idempotent.
