# Messaging

RabbitMQ adapters for the Orders module:

- Message handlers (`IMessageHandler<T>`) for the integration events this module consumes. They translate a
  message into an Application command; no business logic here.
- Consumer registration: `AddOrdersMessaging()` in `DependencyInjection/`, one `AddRabbitMqConsumer` per queue.
  Queues are named `orders.<purpose>`.
- Outbox (later): reliable publishing of this module's integration events.

Application code publishes through `IMessagePublisher` (BuildingBlocks.Abstractions) and never references
RabbitMQ.Client. See docs/messaging.md.
