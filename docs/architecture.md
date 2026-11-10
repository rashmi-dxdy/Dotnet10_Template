# Architecture

This template structures each microservice with Domain-Driven Design (DDD) and Command Query
Responsibility Segregation (CQRS) conventions. It contains **no business features**, only the
boundaries, conventions and abstractions they plug into. `tests/Architecture.Tests` enforces the rules
marked **(enforced)**, so breaking one fails the build pipeline.

## 1. Solution map

```text
src/
  BuildingBlocks/
    BuildingBlocks.Abstractions     Technical contracts: IDomainEvent, IIntegrationEvent, IDateTimeProvider
    BuildingBlocks.Application      CQRS contracts on MediatR 12, logging + FluentValidation behaviors, AddCqrs()
    BuildingBlocks.Hosting          Host setup for every Api/Worker: Serilog, log properties, redaction, shutdown logging
    BuildingBlocks.Infrastructure   Technical implementations shared by services (e.g. SystemDateTimeProvider)
    BuildingBlocks.Web              HTTP API setup: correlation ID, request logging, exception handling, problem details
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
| `Behaviors/` | MediatR pipeline behaviors (logging, validation, transactions) |
| `Abstractions/` | Ports the application needs from the outside (e.g. unit of work, external services) |
| `DependencyInjection/` | `Add<Service>Application()` registration |

### Mediator: MediatR 12

Commands and queries are dispatched with **MediatR 12.5.0**, the last Apache-2.0 release.

- **License:** MediatR 13+ is commercially licensed and needs a license key. The version is pinned in
  `Directory.Packages.props`, and `Architecture.Tests` fails if it is not `12.x` **(enforced)**.
- **Where it lives:** `BuildingBlocks.Application`. `ICommand<T>` / `IQuery<T>` extend `IRequest<T>`, and
  `ICommandHandler<,>` / `IQueryHandler<,>` extend `IRequestHandler<,>`. `BuildingBlocks.Abstractions`
  stays package-free, so Domain never sees MediatR.
- **Registration:** `Add<Service>Application()` calls `services.AddCqrs(assembly)`. That registers
  `ISender`/`IMediator`, scans the Application assembly for handlers and adds the shared behaviors.
- **Dispatch:** endpoints and consumers inject `ISender` and call `Send(command, cancellationToken)`.
- **Validation:** FluentValidation 12 (Apache-2.0). `AddCqrs` registers every `AbstractValidator<T>` in the
  Application assembly. `ValidationBehavior` runs all validators for a request before its handler and
  throws `FluentValidation.ValidationException` with every failure. The API hosts' `ValidationExceptionHandler`
  turns that into a `400` RFC 9457 validation problem response.
- **Behaviors:** implement `IPipelineBehavior<TRequest, TResponse>`. Shared ones (`LoggingBehavior`, then
  `ValidationBehavior`) are in `BuildingBlocks.Application/Behaviors`. Service-specific ones go in the service's `Behaviors/` and are
  added via the `configure` callback of `AddCqrs`. Behaviors run in registration order, outermost first.

```csharp
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> Handle(CreateProductCommand command, CancellationToken cancellationToken) => ...;
}

app.MapPost("/products", (CreateProductCommand cmd, ISender sender, CancellationToken ct) => sender.Send(cmd, ct));
```

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
| `X.Application` | own Domain, `BuildingBlocks.Abstractions`, `BuildingBlocks.Application`, `IntegrationEvents` |
| `X.Infrastructure` | own Application and Domain, `BuildingBlocks.*`, `IntegrationEvents` |
| EF Core / Npgsql runtime packages | `X.Infrastructure` and `BuildingBlocks.Infrastructure` only; EF Design/Tools in hosts only (design-time) |
| Broker clients (`RabbitMQ.Client`, `Confluent.Kafka`) | `X.Infrastructure` and `BuildingBlocks.Infrastructure` only; Domain/Application use `IMessagePublisher` / `IMessageHandler<T>` |
| `X.Api` | own Application and Infrastructure, `BuildingBlocks.*` |
| `X.Worker` | own Application and Infrastructure, `BuildingBlocks.*` except `BuildingBlocks.Web` |
| `BuildingBlocks.Abstractions` | nothing: no projects, no packages |
| `BuildingBlocks.Application` | no projects; packages MediatR 12.x, FluentValidation and logging abstractions |
| `BuildingBlocks.Hosting` | no projects; generic host and Serilog packages, no ASP.NET Core |
| `BuildingBlocks.Web` | `BuildingBlocks.Hosting` only; ASP.NET Core and `Serilog.AspNetCore` |
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
2. **No shared tables.** Each service reads and writes only its own tables, through its own DbContext and
   connection string **(enforced: one DbContext per service, declared only in its Infrastructure)**. For now
   all services share one database, and every table carries its owner's prefix (`CAT_`, `ORD_`, `NTF_`)
   **(enforced)**. Each service will move to its own database later. See [database.md](database.md).
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
