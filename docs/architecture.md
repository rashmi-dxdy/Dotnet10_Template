# Architecture

This template is a **modular monolith**: one deployable application (`src/Host/Host.Api`) that hosts every
module in one process. Each module is a bounded context structured with Domain-Driven Design (DDD) and
Command Query Responsibility Segregation (CQRS), and can be extracted into its own process and database by
configuration alone (see [modules.md](modules.md)). It contains **no business features**, only the
boundaries, conventions and abstractions they plug into. `tests/Architecture.Tests` enforces the rules
marked **(enforced)**, so breaking one fails the build pipeline.

## 1. Solution map

```text
src/
  BuildingBlocks/
    BuildingBlocks.Abstractions     Technical contracts: IDomainEvent, IIntegrationEvent, IDateTimeProvider, module conventions
    BuildingBlocks.Application      CQRS contracts on MediatR 12, logging + FluentValidation behaviors, AddCqrs()
    BuildingBlocks.Hosting          Host setup: Serilog, log properties, redaction, shutdown logging
    BuildingBlocks.Infrastructure   Technical implementations: PostgreSQL, RabbitMQ, Kafka, module HTTP clients
    BuildingBlocks.Web              HTTP setup: correlation ID, request logging, problem details, IModule, AddModules/MapModules
  Contracts/
    IntegrationEvents               Asynchronous messages between modules
    <Module>.Contracts              The module's internal API for other modules: name + request/response DTOs
  Modules/<Module>/
    <Module>.Domain                 Business model
    <Module>.Application            Use cases (commands, queries)
    <Module>.Infrastructure         Adapters: PostgreSQL, RabbitMQ/Kafka, clients of other modules, external APIs
    <Module>.Api                    <Module>Module (IModule): registration + public and internal endpoints
  Host/
    Host.Api                        The one deployable application: composes every module (Program.cs)
```

Modules today: **Catalog**, **Orders**, **Notifications**.

## 2. DDD boundaries

Each module is a **bounded context**. It owns its model, its language and its data.

| Boundary | Rule |
|---|---|
| Module | Owns its tables (its prefix in the shared database). No other module reads or writes them. Other modules reach it only through its internal API and integration events. |
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
| `DependencyInjection/` | `Add<Module>Application()` registration |

### Mediator: MediatR 12

Commands and queries are dispatched with **MediatR 12.5.0**, the last Apache-2.0 release.

- **License:** MediatR 13+ is commercially licensed and needs a license key. The version is pinned in
  `Directory.Packages.props`, and `Architecture.Tests` fails if it is not `12.x` **(enforced)**.
- **Where it lives:** `BuildingBlocks.Application`. `ICommand<T>` / `IQuery<T>` extend `IRequest<T>`, and
  `ICommandHandler<,>` / `IQueryHandler<,>` extend `IRequestHandler<,>`. `BuildingBlocks.Abstractions`
  stays package-free, so Domain never sees MediatR.
- **Registration:** `Add<Module>Application()` calls `services.AddCqrs(assembly)`. That registers
  `ISender`/`IMediator`, scans the Application assembly for handlers and adds the shared behaviors. All modules
  share one container; `AddCqrs` is safe to call once per module (shared behaviors are registered once).
- **Dispatch:** endpoints and consumers inject `ISender` and call `Send(command, cancellationToken)`.
- **Validation:** FluentValidation 12 (Apache-2.0). `AddCqrs` registers every `AbstractValidator<T>` in the
  Application assembly. `ValidationBehavior` runs all validators for a request before its handler and
  throws `FluentValidation.ValidationException` with every failure. `ValidationExceptionHandler`
  (`BuildingBlocks.Web`, registered once by the host) turns that into a `400` RFC 9457 validation problem response.
- **Behaviors:** implement `IPipelineBehavior<TRequest, TResponse>`. Shared ones (`LoggingBehavior`, then
  `ValidationBehavior`) are in `BuildingBlocks.Application/Behaviors`. Module-specific ones go in the module's
  `Behaviors/` and are added via the `configure` callback of `AddCqrs`. Behaviors run in registration order, outermost first.

```csharp
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> Handle(CreateProductCommand command, CancellationToken cancellationToken) => ...;
}

// In CatalogModule.MapEndpoints: POST /api/catalog/products
routes.Public.MapPost("products", (CreateProductCommand cmd, ISender sender, CancellationToken ct) => sender.Send(cmd, ct));
```

## 4. Dependency inversion

Dependencies point **inward**, toward the domain. Outer layers implement interfaces that inner layers
define.

```text
   Host.Api  (composition root: composes the modules, nothing else)
      │
      ▼
   <Module>.Api  (IModule: registration + endpoints)
      │        │
      ▼        ▼
Infrastructure ──► Application ──► Domain ──► BuildingBlocks.Abstractions
      │    │            │                         ▲
      │    │            └──► IntegrationEvents ───┤
      │    └──► <Other>.Contracts (HTTP clients) ─┘
      ▼
BuildingBlocks.Infrastructure ──► BuildingBlocks.Abstractions
```

| Project | May reference **(enforced)** |
|---|---|
| `X.Domain` | `BuildingBlocks.Abstractions` only, no packages |
| `X.Application` | own Domain, own `X.Contracts`, `BuildingBlocks.Abstractions`, `BuildingBlocks.Application`, `IntegrationEvents` |
| `X.Infrastructure` | own Application and Domain, **any** `<Module>.Contracts` (to call other modules), `BuildingBlocks.Abstractions/Application/Infrastructure`, `IntegrationEvents` |
| `X.Api` | own Application, Infrastructure and `X.Contracts`, `BuildingBlocks.Abstractions/Application/Hosting/Web` |
| `Host.Api` | `<Module>.Api` projects, `BuildingBlocks.Hosting`, `BuildingBlocks.Web` only |
| Another module's Domain / Application / Infrastructure / Api | **never**, from any module, also not by name in code |
| EF Core / Npgsql runtime packages | `X.Infrastructure` and `BuildingBlocks.Infrastructure` only; EF Design/Tools in the host only (design-time) |
| Broker clients (`RabbitMQ.Client`, `Confluent.Kafka`) | `X.Infrastructure` and `BuildingBlocks.Infrastructure` only; Domain/Application use `IMessagePublisher` / `IMessageHandler<T>` (RabbitMQ) and `IEventStreamProducer` / `IStreamMessageHandler<T>` (Kafka) |
| `BuildingBlocks.Abstractions` | nothing: no projects, no packages |
| `BuildingBlocks.Application` | no projects; packages MediatR 12.x, FluentValidation and logging abstractions |
| `BuildingBlocks.Hosting` | no projects; generic host and Serilog packages, no ASP.NET Core |
| `BuildingBlocks.Web` | `BuildingBlocks.Hosting` and `BuildingBlocks.Abstractions`; ASP.NET Core, `Serilog.AspNetCore`, FluentValidation |
| `IntegrationEvents`, `<Module>.Contracts` | `BuildingBlocks.Abstractions` only, no packages |

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
| `ExternalServices/` | Clients of other modules' internal APIs (`AddModuleClient`) and third-party clients |
| `DependencyInjection/` | `Add<Module>Infrastructure(configuration)` registration |

### Api folder conventions **(enforced)**

| Folder / file | Contents |
|---|---|
| `<Module>Module.cs` | The `IModule` implementation: name, `AddServices`, `MapEndpoints` |
| `Endpoints/` | Public (`/api/<module>`) and internal (`/internal/<module>`) endpoints; no logic, they send commands and queries |

### Registration extension points

The host composes the modules in `src/Host/Host.Api/Program.cs`; each module composes its own layers:

```csharp
builder.AddModules(new CatalogModule(), new OrdersModule(), new NotificationsModule());

// Catalog.Api/CatalogModule.cs (called only if Modules:Catalog:Enabled)
public void AddServices(IServiceCollection services, IConfiguration configuration) =>
    services
        .AddCatalogApplication()                          // Application/DependencyInjection
        .AddCatalogInfrastructure(configuration);         // Infrastructure/DependencyInjection
                                                          //   └─ AddBuildingBlocksInfrastructure()
```

`AddBuildingBlocksInfrastructure` registers `TimeProvider.System` and `IDateTimeProvider` with
`TryAdd*`, so tests and hosts can register their own replacements first. Shared registrations (RabbitMQ
connection, Kafka producer) are idempotent: the first module registers them, the others reuse them.

## 5. Domain events versus integration events

| | Domain event | Integration event |
|---|---|---|
| Interface | `IDomainEvent` (`BuildingBlocks.Abstractions.Domain`) | `IIntegrationEvent` (`BuildingBlocks.Abstractions.Messaging`) |
| Defined in | `<Module>.Domain/Events` | `src/Contracts/IntegrationEvents` |
| Audience | The same module only | Other modules |
| Transport | In-process, same transaction scope | Message broker (RabbitMQ or Kafka), asynchronous |
| Language | The module's internal model | A stable public contract |
| Versioning | Free to change with the model | Backward compatible: add fields, never repurpose or remove them |
| Delivery | Immediate | At least once, so consumers must be **idempotent** (use `EventId`) |

**Flow:** an aggregate raises a domain event. An application-layer handler reacts inside the module
and, if other modules need to know, maps it to an integration event. Infrastructure then publishes
that event, ideally through a transactional outbox so the state change and the message can't
disagree.

Never publish a domain event type to the broker. That would turn the internal model into a public
contract.

## 6. Cross-module communication rules

Modules run in one process today, but are built as if they didn't. See [modules.md](modules.md) for details
and examples.

1. **No project references between modules** **(enforced)**. A module never references another module's
   Domain, Application, Infrastructure or Api, and never names them in code **(enforced)**.
2. **No shared tables.** Each module reads and writes only its own tables, through its own DbContext and
   connection string **(enforced: one DbContext per module, declared only in its Infrastructure)**. For now
   all modules share one database, and every table carries its owner's prefix (`CAT_`, `ORD_`, `NTF_`)
   **(enforced)**. Each module can move to its own database later. See [database.md](database.md).
3. **Asynchronous first.** Modules communicate through integration events from
   `src/Contracts/IntegrationEvents` over RabbitMQ or Kafka.
4. **Synchronous calls go through the internal HTTP API.** When a module needs an answer now, it calls the
   other module's `/internal/<module>/...` endpoints through a port in `Application/Abstractions` and a module
   client (`AddModuleClient`) in `Infrastructure/ExternalServices`, with the DTOs from `<Module>.Contracts`. Calls
   go over HTTP even inside the monolith, so nothing changes when a module is extracted. Never through in-process
   MediatR requests, shared services or another module's types.
5. **Contracts only.** `IntegrationEvents` and `<Module>.Contracts` contain records and constants and nothing
   else: no logic, no module types, and no dependencies except `BuildingBlocks.Abstractions` **(enforced)**.
6. **Consumers are idempotent and tolerant.** Deduplicate by `EventId`, and ignore unknown fields.
7. **Contracts evolve compatibly.** Add fields; never rename or remove them. Once a module is extracted, its
   callers upgrade independently.
8. **Each module is extractable.** Where a module runs is configuration (`Modules:<Module>:Enabled`,
   `Modules:<Module>:BaseAddress`), not code. See [modules.md](modules.md), section 7.
