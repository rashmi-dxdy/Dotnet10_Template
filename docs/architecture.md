# Architecture

This template structures each microservice with Domain-Driven Design (DDD) and Command Query
Responsibility Segregation (CQRS) conventions. It contains **no business features**, only the
boundaries, conventions and abstractions they plug into. `tests/Architecture.Tests` enforces the rules
marked **(enforced)**, so breaking one fails the build pipeline.

## 1. Solution map

```text
src/
  BuildingBlocks/
    BuildingBlocks.Abstractions     Technical contracts: CQRS, IDomainEvent, IIntegrationEvent, IDateTimeProvider
    BuildingBlocks.Infrastructure   Technical implementations shared by services (e.g. SystemDateTimeProvider)
  Contracts/
    IntegrationEvents               Cross-service message contracts only
  Services/<Service>/
    <Service>.Domain                Business model
    <Service>.Application           Use cases (commands, queries)
    <Service>.Infrastructure        Adapters: PostgreSQL, RabbitMQ/Kafka, external APIs
    <Service>.Api | .Worker         Composition root (host)
```

## 2. DDD boundaries

Each service is a **bounded context**. It owns its model, its language and its data.

| Boundary | Rule |
|---|---|
| Service | Owns its database. No other service reads or writes it. |
| Aggregate | The consistency boundary. One aggregate is changed per transaction. Other aggregates are referenced by ID only. |
| Domain project | Pure C#. No EF Core, no broker clients, no ASP.NET, no packages **(enforced)**. |
| BuildingBlocks | Technical concerns only. Never business entities or business rules. |

### Domain folder conventions **(enforced)**

| Folder | Contents |
|---|---|
| `Aggregates/` | Aggregate roots, the only entry point for changes |
| `Entities/` | Entities inside an aggregate that are not roots |
| `ValueObjects/` | Immutable types defined by their values, validated on creation |
| `Events/` | Domain events (`IDomainEvent`) |
| `Exceptions/` | Exceptions for violated business invariants |
| `Repositories/` | Repository **interfaces**, one per aggregate root |

Each folder contains a `README.md` with the same guidance.

## 3. CQRS separation

Writes and reads are separate use cases with separate types:

| | Command | Query |
|---|---|---|
| Purpose | Change state | Read state |
| Contract | `ICommand<TResult>` | `IQuery<TResult>` |
| Handler | `ICommandHandler<TCommand, TResult>` | `IQueryHandler<TQuery, TResult>` |
| Goes through | The domain model (aggregates, repositories) | A read model or projection is fine; skipping aggregates is allowed |
| Returns | Minimal result (ID, outcome) | DTO shaped for the caller |
| Side effects | Yes, inside one transaction | **Never** |

### Application folder conventions **(enforced)**

| Folder | Contents |
|---|---|
| `Commands/`, `Queries/` | One sub-folder per use case: request record, handler and (for queries) the DTO |
| `Validators/` | Input validation (shape and format). Business invariants stay in the Domain. |
| `Behaviors/` | Cross-cutting decorators around handlers (logging, validation, transactions) |
| `Abstractions/` | Ports the application needs from the outside (e.g. unit of work, external services) |
| `DependencyInjection/` | `Add<Service>Application()` registration |

### No mediator

The template deliberately has **no mediator library and no custom dispatcher**:

- MediatR moved to a commercial license from v13, and other mediator packages had no license
  information that could be verified from their package metadata when this template was created.
- Handlers are registered in DI as `ICommandHandler<,>` / `IQueryHandler<,>`. An endpoint or consumer
  injects the specific handler it needs. That keeps each call explicit and easy to navigate.
- Cross-cutting behavior is added with decorators around handler interfaces, registered in
  `DependencyInjection/`.

To adopt a mediator later, first check its current license and .NET 10 compatibility, then add it to
`Directory.Packages.props`.

## 4. Dependency inversion

Dependencies point **inward**, toward the domain. Outer layers implement interfaces that inner layers
define.

```text
   Api / Worker  (composition root: wires everything together)
      │        │
      ▼        ▼
Infrastructure ──► Application ──► Domain ──► BuildingBlocks.Abstractions
      │                 │                         ▲
      ▼                 └──► IntegrationEvents ───┘
BuildingBlocks.Infrastructure ──► BuildingBlocks.Abstractions
```

| Project | May reference **(enforced)** |
|---|---|
| `X.Domain` | `BuildingBlocks.Abstractions` only, no packages |
| `X.Application` | own Domain, `BuildingBlocks.Abstractions`, `IntegrationEvents` |
| `X.Infrastructure` | own Application and Domain, `BuildingBlocks.*`, `IntegrationEvents` |
| `X.Api` / `X.Worker` | own Application and Infrastructure, `BuildingBlocks.*` |
| `BuildingBlocks.Abstractions` | nothing: no projects, no packages |
| `IntegrationEvents` | `BuildingBlocks.Abstractions` only, no packages |

Examples of the inversion:

- `Domain/Repositories/IOrderRepository` is defined in the Domain and implemented in
  `Infrastructure/Persistence` with EF Core.
- `Application/Abstractions/IPaymentGateway` is defined in the Application and implemented in
  `Infrastructure/ExternalServices`.
- `IDateTimeProvider` is defined in `BuildingBlocks.Abstractions` and implemented by
  `SystemDateTimeProvider` in `BuildingBlocks.Infrastructure`, which wraps .NET `TimeProvider`.

(`IOrderRepository` and `IPaymentGateway` are illustrative names only. They don't exist in the template.)

### Infrastructure folder conventions **(enforced)**

| Folder | Contents |
|---|---|
| `Persistence/` | `DbContext`, entity configurations, migrations, repository implementations |
| `Messaging/` | RabbitMQ/Kafka publishers and consumers, outbox |
| `ExternalServices/` | HTTP and third-party clients |
| `DependencyInjection/` | `Add<Service>Infrastructure(configuration)` registration |

### Registration extension points

Each host composes its service in `Program.cs`:

```csharp
builder.Services
    .AddCatalogApplication()                              // Application/DependencyInjection
    .AddCatalogInfrastructure(builder.Configuration);     // Infrastructure/DependencyInjection
                                                          //   └─ AddBuildingBlocksInfrastructure()
```

`AddBuildingBlocksInfrastructure` registers `TimeProvider.System` and `IDateTimeProvider` with
`TryAdd*`, so tests and hosts can register their own replacements first.

## 5. Domain events versus integration events

| | Domain event | Integration event |
|---|---|---|
| Interface | `IDomainEvent` (`BuildingBlocks.Abstractions.Domain`) | `IIntegrationEvent` (`BuildingBlocks.Abstractions.Messaging`) |
| Defined in | `<Service>.Domain/Events` | `src/Contracts/IntegrationEvents` |
| Audience | The same service only | Other services |
| Transport | In-process, same transaction scope | Message broker (RabbitMQ or Kafka), asynchronous |
| Language | The service's internal model | A stable public contract |
| Versioning | Free to change with the model | Backward compatible: add fields, never repurpose or remove them |
| Delivery | Immediate | At least once, so consumers must be **idempotent** (use `EventId`) |

**Flow:** an aggregate raises a domain event. An application-layer handler reacts inside the service
and, if other services need to know, maps it to an integration event. Infrastructure then publishes
that event, ideally through a transactional outbox so the state change and the message can't
disagree.

Never publish a domain event type to the broker. That would turn the internal model into a public
contract.

## 6. Cross-service communication rules

1. **No project references between services** **(enforced)**. A service never references another
   service's Domain, Application or Infrastructure.
2. **No shared databases.** Each service reads and writes only its own database.
3. **Asynchronous first.** Services communicate through integration events from
   `src/Contracts/IntegrationEvents` over RabbitMQ or Kafka.
4. **Contracts only.** `IntegrationEvents` contains message records and nothing else: no logic, no
   service types, and no dependencies except `BuildingBlocks.Abstractions` **(enforced)**.
5. **Consumers are idempotent and tolerant.** Deduplicate by `EventId`, and ignore unknown fields.
6. **Synchronous calls are the exception.** If a service must call another synchronously (HTTP/gRPC),
   it does so through an interface in `Application/Abstractions` with a client in
   `Infrastructure/ExternalServices`, with timeouts and resilience. Never through a shared library.
7. **Each service deploys independently.** A change to one service must not require redeploying
   another. Contract changes must stay backward compatible.
