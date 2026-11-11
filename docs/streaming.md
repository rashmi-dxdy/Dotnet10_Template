# Event streaming: Apache Kafka

Services can append to and read from Kafka topics through Confluent.Kafka 2.16 (Apache-2.0). Kafka is configured
**independently of RabbitMQ**: its own abstractions, its own `Kafka` configuration section and its own health check.

## 1. Kafka or RabbitMQ?

| | RabbitMQ (`docs/messaging.md`) | Kafka (this document) |
|---|---|---|
| Model | Queue: a message is removed once acknowledged | Log: messages stay for the retention period and can be re-read |
| Use for | Integration events, commands, work distribution | Ordered event streams, change feeds, analytics, audit, replay |
| Ordering | Not guaranteed across redeliveries | Guaranteed per partition (per key) |
| Failure handling | Per message: ack / nack + requeue / dead-letter | Retry in-process, then dead-letter topic; the offset moves on |
| Abstractions | `IMessagePublisher`, `IMessageHandler<T>` | `IEventStreamProducer`, `IStreamMessageHandler<T>` |

## 2. Layers

| Layer | What | Confluent.Kafka? |
|---|---|---|
| `BuildingBlocks.Abstractions/Streaming` | `IEventStreamProducer`, `IStreamMessageHandler<T>`, `StreamMessageContext<T>`, `StreamProduceException` | No (pure C#) |
| Domain, Application | Produce through `IEventStreamProducer`; handle through `IStreamMessageHandler<T>` | **No (enforced)** |
| `BuildingBlocks.Infrastructure/Streaming/Kafka` | Producer, consumer, admin/topic provisioning, health check, lifecycle | Yes |
| `<Service>.Infrastructure` | `Add<Service>Streaming()`: registers the adapter and the service's consumers | Yes |

`Architecture.Tests` fails if any project other than an Infrastructure project references Confluent.Kafka, or if
Domain/Application/Abstractions code contains `using Confluent` **(enforced)**.

## 3. Configuration

Section `Kafka`, overridden by `Kafka__*` environment variables (nested settings use `__`, e.g.
`Kafka__Consumer__GroupId`). Secrets are never committed (`SaslPassword` must be empty in appsettings, **enforced**).

| Setting | Default | Meaning |
|---|---|---|
| `BootstrapServers` | `localhost:9092` | Comma-separated `host:port` list |
| `ClientId` | service name | Shown in broker logs and metrics |
| `BrokerAddressFamily` | `Any` (`V4` in Development) | See the Windows note below |
| `SecurityProtocol` | `Plaintext` | `Plaintext`, `Ssl`, `SaslPlaintext`, `SaslSsl` |
| `SaslMechanism` / `SaslUsername` / `SaslPassword` | `Plain` / – / – | Required for `Sasl*` (`Plain`, `ScramSha256`, `ScramSha512`) |
| `SslCaLocation` | – | CA file to verify brokers with `Ssl`/`SaslSsl` |
| `Producer:Acks` / `EnableIdempotence` | `All` / `true` | See section 4 |
| `Producer:MessageTimeoutMs` / `LingerMs` / `CompressionType` | `30000` / `5` / `Lz4` | |
| `Consumer:GroupId` | per service: `catalog`, `orders`, `notifications` | See section 6 |
| `Consumer:AutoOffsetReset` | `Earliest` | Where a **new** group starts |
| `Consumer:MaxRetryAttempts` / `RetryBackoffMs` | `3` / `1000` | See section 7 |
| `ProvisionTopics` | `false` (`true` in Development) | Create missing topics at startup |
| `TopicDefaults:Partitions` / `ReplicationFactor` | `3` / `1` | For provisioned topics |
| `Topics` | `[]` | Topics this service owns (section 5) |

Invalid settings stop the service at startup with a list of what to fix and the environment variable for each.
Production with SASL over TLS, for example:

```yaml
environment:
  Kafka__BootstrapServers: "kafka-1:9093,kafka-2:9093,kafka-3:9093"
  Kafka__SecurityProtocol: SaslSsl
  Kafka__SaslMechanism: ScramSha512
  Kafka__SaslUsername: ${KAFKA_SASL_USERNAME}
  Kafka__SaslPassword: ${KAFKA_SASL_PASSWORD}
  Kafka__TopicDefaults__ReplicationFactor: "3"
```

> **Windows + Docker:** `localhost` resolves to IPv6 `::1` first, and Docker usually publishes the Kafka port on
> IPv4 only. librdkafka then alternates between the two addresses and connections fail intermittently (timeouts,
> "brokers are down"). `BrokerAddressFamily: V4` (the Development default) avoids it. For the CLI or tests, set
> `Kafka__BrokerAddressFamily=V4`.

## 4. Producing

```csharp
await producer.ProduceAsync("orders.order-events", key: order.Id.ToString(), new OrderShipped(...), correlationId, ct);
```

Reliable settings for the template:

- `acks=all`: the write is acknowledged only once all in-sync replicas have it.
- **Idempotent producer**: client retries never duplicate or reorder messages within a partition.
- Automatic retries for up to `MessageTimeoutMs` (30 s). `ProduceAsync` completes on acknowledgement and throws
  `StreamProduceException` if the message could not be delivered.
- **One producer per service** (thread-safe, keeps its broker connections), flushed on shutdown.
- JSON (camelCase) body. Headers `message-id` (the event's `EventId`), `correlation-id`, `message-type`,
  `content-type`.
- **Key = partition = order.** Use the aggregate ID as the key when order matters. `null` spreads the load
  without ordering.
- **Cancellation:** the token stops *waiting*. A message already handed to the client may still be delivered.

## 5. Topics: naming and how to create them

| Topic | Convention | Example | Owner |
|---|---|---|---|
| Event stream | `<producing-service>.<stream>` | `orders.order-events` | The producing service |
| Dead-letter topic | `<topic>.<consumer-group>.dlt` | `orders.order-events.notifications.dlt` | The consuming group |

The template creates **no business topics**. When a service starts producing a stream, it declares the topic in
its own `appsettings.json`:

```json
"Kafka": {
  "Topics": [
    { "Name": "orders.order-events", "Partitions": 6, "RetentionMs": 604800000 }
  ]
}
```

How topics get created:

| Environment | How |
|---|---|
| **Development** | `ProvisionTopics: true` (Development default): at startup the service creates its missing `Topics` and its consumers' dead-letter topics. Existing topics are never changed. |
| **CLI** (any environment) | `kafka-topics.sh --bootstrap-server localhost:9092 --create --topic orders.order-events --partitions 6 --replication-factor 3 --config retention.ms=604800000` |
| **Production** | Infrastructure as code (Terraform, Strimzi `KafkaTopic`, Confluent Cloud, …), with `ProvisionTopics: false`. Services should not need topic-creation rights. |

Choosing values:

- **Partitions** set the maximum parallelism of one consumer group (one partition is read by at most one member).
  You can add partitions later but not remove them, and adding them changes which partition a key maps to.
  Start with 3–12.
- **Replication factor**: `3` in production (with `min.insync.replicas=2`), `1` for a single local broker.
- **Retention**: how long consumers can replay. Default broker retention is 7 days.

Inspect topics:

```bash
kafka-topics.sh --bootstrap-server localhost:9092 --list
kafka-topics.sh --bootstrap-server localhost:9092 --describe --topic orders.order-events
```

## 6. Consumer groups

A **consumer group** is a named set of consumers that share the work of reading a topic. Kafka assigns each
partition to exactly one member of the group, and tracks the group's position (**committed offset**) per
partition.

```text
topic orders.order-events (3 partitions)
  P0 ──► notifications instance A ┐
  P1 ──► notifications instance A ├─ group "notifications": every message handled ONCE by the group
  P2 ──► notifications instance B ┘
  P0, P1, P2 ──► catalog instance A ── group "catalog": gets ALL messages again, independently
```

- **One group per service** (`Kafka:Consumer:GroupId`). All instances of a service share it, so scaling out
  divides the partitions between them. More instances than partitions leaves the extra ones idle.
- **Different services use different groups**, so each service receives every message (publish/subscribe).
- `AddKafkaConsumer<T, THandler>(topic, groupId: "...")` gives one consumer its own group, when it must track
  offsets separately (e.g. a projection you want to rebuild by resetting its offsets).
- **Rebalancing:** when instances join or leave, partitions are reassigned. The template uses the
  *cooperative-sticky* assignor, so only the moved partitions pause. Stored offsets are committed before a
  partition is revoked.
- **New group:** starts at `AutoOffsetReset` (`Earliest` = the full retained history; `Latest` = only new
  messages). After its first commit, the group always resumes from its committed offsets.

Inspect or reset a group (stop its instances first for a reset):

```bash
kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group notifications
kafka-consumer-groups.sh --bootstrap-server localhost:9092 --group notifications --topic orders.order-events \
  --reset-offsets --to-earliest --execute     # or --to-datetime 2026-11-01T00:00:00.000 / --shift-by -100
```

## 7. Consuming, offsets and errors

```csharp
// Infrastructure/Messaging/OrderEventsHandler.cs
internal sealed class OrderEventsHandler(ISender sender) : IStreamMessageHandler<OrderShipped>
{
    public Task HandleAsync(StreamMessageContext<OrderShipped> context, CancellationToken ct) =>
        sender.Send(new RecordShipment(context.Message.OrderId), ct);
}

// DependencyInjection/StreamingServiceCollectionExtensions.cs, in AddNotificationsStreaming
services.AddKafkaConsumer<OrderShipped, OrderEventsHandler>("orders.order-events");
```

- Messages are handled **one at a time per consumer, in partition order**, each in a new DI scope. Log entries
  carry `MessageId`, `CorrelationId`, `Topic`, `Partition` and `Offset`.
- **Offset management (at-least-once):** the offset is stored only after the handler completed (or the message was
  dead-lettered). Stored offsets are committed every `AutoCommitIntervalMs` (5 s), before a partition is
  revoked, and on shutdown. After a crash, messages since the last commit are delivered again, so handlers must be
  idempotent (deduplicate by `MessageId`).
- **Errors:**

| Situation | What happens |
|---|---|
| Handler throws | Retried `MaxRetryAttempts` times with exponential backoff (1 s, 2 s, 4 s). `context.Attempt` tells the handler which attempt it is |
| Still failing | Sent to `<topic>.<group>.dlt` with headers `dlt-original-topic/-partition/-offset`, `dlt-consumer-group`, `dlt-reason`, `dlt-exception-type`. The offset moves on, so the partition is not blocked |
| Body is not valid JSON | Sent to the dead-letter topic immediately |
| Dead-letter topic unavailable | The message is **not** skipped: it is retried every 5 s (the partition waits), logged as Critical |
| Shutdown during a retry backoff | The offset is not committed; the message is processed again after restart |

Replaying dead letters: fix the cause, then consume the `.dlt` topic with a small tool or handler and produce the
messages back to the original topic.

## 8. Lifecycle, reconnection and shutdown

- **Startup:** in the background the service checks the cluster and, with `ProvisionTopics`, creates missing
  topics. It starts even if Kafka is down:

  ```text
  [ERR] Kafka localhost:9092 (Plaintext) is unavailable: Local: Broker transport failure (Local_Transport). Is Kafka
        running (e.g. `docker compose up -d kafka`) and is Kafka:BootstrapServers correct? ...
  [INF] Connected to Kafka localhost:9092 (Plaintext): 1 broker(s)
  ```

- **Connection loss:** librdkafka reconnects automatically. Client errors are logged once a minute per error code,
  with a count of suppressed repeats, so an outage does not flood the log.
- **Graceful shutdown:** consumers stop reading, finish the in-flight message (until `HostOptions:ShutdownTimeout`),
  commit offsets and **leave the group**, so their partitions are reassigned immediately. Then the producer is
  flushed. This was measured at under 3 s, also with the cluster unreachable.

## 9. Health checks

`kafka` (tag `ready`) asks the cluster for its brokers within `AdminTimeoutSeconds` (5 s). Unhealthy while
unreachable. It runs on `/health/ready`, next to PostgreSQL and RabbitMQ.

## 10. Smoke tests

`tests/BuildingBlocks.IntegrationTests/Streaming/KafkaSmokeTests.cs` runs the adapter against a **real** cluster
with a generic test event. It covers produce and consume with key, headers and position; per-key ordering;
committed offsets surviving a restart; retries then dead-letter topic; the health check; and cancellation. Each
test uses its own topic and consumer group and deletes its topics afterwards. Without `Kafka__BootstrapServers` the
tests are **skipped**.

```bash
docker run -d --name kafka-dev -p 127.0.0.1:9092:9092 apache/kafka:4.4.0     # single-node KRaft
```

```powershell
$env:Kafka__BootstrapServers = 'localhost:9092'; $env:Kafka__BrokerAddressFamily = 'V4'
dotnet test --project tests/BuildingBlocks.IntegrationTests
```

In Git Bash, prefix `docker exec` commands with `MSYS_NO_PATHCONV=1`, e.g.
`MSYS_NO_PATHCONV=1 docker exec kafka-dev /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list`.

## 11. Adding a stream to a service

1. Producer: add the topic to the service's `Kafka:Topics`, inject `IEventStreamProducer` in the Application layer
   and produce with a meaningful key.
2. Consumer: write an `IStreamMessageHandler<T>` in the consuming service's `Infrastructure/Messaging` that sends
   an Application command. Register it with `AddKafkaConsumer` in `Add<Service>Streaming`, and make it idempotent.
3. Production: create the topic (and the consumer's `.dlt` topic) with infrastructure tooling.
