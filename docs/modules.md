# Modular monolith: modules, the host and module communication

The solution is **one deployable application** (`src/Host/Host.Api`) that runs every module in a single
process. The modules share one PostgreSQL database, but each owns only its prefixed tables. They call each
other **only** through internal HTTP APIs and integration events, never through each other's types or tables.

Those rules exist so that any module can later move to **its own process and its own database by changing
configuration, not code**. Section 7 has the step-by-step guide.

## 1. Shape

```text
                        ┌──────────────────────── Host.Api (one process) ────────────────────────┐
  external clients ───► │  /api/catalog/...        /api/orders/...        /api/notifications/...  │
   (via gateway)        │  ┌─────────────┐  HTTP   ┌─────────────┐        ┌──────────────────┐   │
                        │  │   Catalog   │◄────────│   Orders    │        │  Notifications   │   │
                        │  │   module    │/internal│   module    │        │     module       │   │
                        │  └──────┬──────┘ catalog └──────┬──────┘        └────────┬─────────┘   │
                        └─────────┼───────────────────────┼────────────────────────┼─────────────┘
                                  │ CAT_*                 │ ORD_*                  │ NTF_*
                        ┌─────────▼───────────────────────▼────────────────────────▼─────────────┐
                        │                     one PostgreSQL database                            │
                        └────────────────────────────────────────────────────────────────────────┘
                          + integration events over RabbitMQ / Kafka (asynchronous, between modules)
```

| Concern | In the monolith today | After a module is extracted |
|---|---|---|
| Deployment | One `Host.Api` | The same `Host.Api` build, deployed again with a different module enabled |
| Synchronous calls | HTTP to `/internal/<module>/...` on the same process (loopback) | HTTP to the same paths on another process |
| Asynchronous calls | Integration events (RabbitMQ / Kafka) | Unchanged |
| Data | One database, tables prefixed `CAT_` / `ORD_` / `NTF_` | The module's tables move to its own database |
| What changes | — | Configuration only: `Modules:*`, `InternalApi:ApiKey`, the connection string |

## 2. Module anatomy

```text
src/Modules/<Module>/
  <Module>.Domain           Business model (pure C#)
  <Module>.Application      Commands, queries, validators, ports (MediatR 12 + FluentValidation)
  <Module>.Infrastructure   PostgreSQL (own DbContext + prefix), RabbitMQ/Kafka, clients of OTHER modules
  <Module>.Api              <Module>Module : IModule, and Endpoints/ (public + internal HTTP)
src/Contracts/<Module>.Contracts
                            <Module>ModuleApi.Name + DTOs of the module's internal API (what other modules see)
src/Host/Host.Api           Program.cs: AddModules(new CatalogModule(), new OrdersModule(), ...)
```

The DDD/CQRS layering inside a module is unchanged (see [architecture.md](architecture.md)). A module adds two
things on top: an `IModule` entry point and a contracts project.

### The `IModule` entry point

```csharp
public sealed class CatalogModule : IModule
{
    public string Name => CatalogModuleApi.Name;                 // "catalog", from Catalog.Contracts

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddCatalogApplication().AddCatalogInfrastructure(configuration);

    public void MapEndpoints(ModuleRouteGroups routes)
    {
        ProductEndpoints.Map(routes.Public);                     // /api/catalog/...
        ProductInternalEndpoints.Map(routes.Internal);           // /internal/catalog/...
    }
}
```

(`ProductEndpoints` and `ProductInternalEndpoints` are illustrative only; the template has no endpoints.)

The host composes every module explicitly, so nothing is discovered by reflection:

```csharp
builder.AddModules(new CatalogModule(), new OrdersModule(), new NotificationsModule());
...
app.MapModules();
```

`AddModules` calls `AddServices` and `MapModules` calls `MapEndpoints` **only for modules enabled in this process**
(section 6).

### Routes

| Group | Path | For | Notes |
|---|---|---|---|
| Public | `/api/<module>/...` | External clients, through the gateway | Listed in OpenAPI under the module's tag |
| Internal | `/internal/<module>/...` | Other modules only | Requires the internal API key (section 4). Hidden from OpenAPI. **Block `/internal/*` at the gateway / ingress.** |

Every request to a module endpoint gets the `Module` log property (e.g. `catalog`), so one module's logs can be
filtered while all modules share a process. FluentValidation failures from any module become `400` problem
details (`BuildingBlocks.Web/ExceptionHandling/ValidationExceptionHandler`).

## 3. Calling another module

There are two ways for modules to talk. Prefer the asynchronous one.

| Need | Use |
|---|---|
| Tell others something happened; they react in their own time | **Integration event** (`src/Contracts/IntegrationEvents`) over RabbitMQ ([messaging.md](messaging.md)) or Kafka ([streaming.md](streaming.md)) |
| Need an answer now (a query, or a command whose result decides what you do next) | **Internal HTTP API** of the other module, through a module client |

Modules never:

- reference another module's Domain, Application, Infrastructure or Api project **(enforced)**;
- name another module's layers in code, even fully qualified **(enforced)**;
- read or write another module's tables (each DbContext maps only its own prefix) **(enforced)**;
- send another module's MediatR commands or queries in-process. They can't anyway, because they never see the
  types.

### Step by step: Orders asks Catalog for a product

1. **Catalog exposes it.** Add the DTO to `Catalog.Contracts` and an internal endpoint to `Catalog.Api`:

   ```csharp
   // src/Contracts/Catalog.Contracts/Products/ProductSummary.cs
   public sealed record ProductSummary(Guid Id, string Name, decimal Price);

   // Catalog.Api/Endpoints/ProductInternalEndpoints.cs
   internal static class ProductInternalEndpoints
   {
       public static void Map(RouteGroupBuilder group) =>
           group.MapGet("products/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
               await sender.Send(new GetProductSummaryQuery(id), ct) is { } product
                   ? Results.Ok(product)
                   : Results.NotFound());
   }
   ```

   `Catalog.Application` may reference its own `Catalog.Contracts`, so the query can return the DTO directly.

2. **Orders defines what it needs**, in its own words, as a port in the Application layer:

   ```csharp
   // Orders.Application/Abstractions/ICatalogGateway.cs
   public interface ICatalogGateway
   {
       Task<OrderableProduct?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
   }
   ```

3. **Orders implements it over HTTP** in Infrastructure. This is the only place that sees `Catalog.Contracts`;
   it maps the other module's DTO to Orders' own type (an anti-corruption layer):

   ```csharp
   // Orders.Infrastructure/ExternalServices/CatalogHttpGateway.cs
   internal sealed class CatalogHttpGateway(HttpClient http) : ICatalogGateway
   {
       public async Task<OrderableProduct?> FindProductAsync(Guid productId, CancellationToken ct)
       {
           using var response = await http.GetAsync(new Uri($"products/{productId}", UriKind.Relative), ct);
           if (response.StatusCode == HttpStatusCode.NotFound)
           {
               return null;
           }

           await response.EnsureModuleSuccessAsync(CatalogModuleApi.Name, ct);
           var product = await response.Content.ReadFromJsonAsync<ProductSummary>(ct);
           return new OrderableProduct(product!.Id, product.Price);
       }
   }
   ```

   Use relative paths **without a leading slash**: the client's base address already ends with
   `/internal/catalog/`.

4. **Register it** in `AddOrdersInfrastructure` (add a project reference from `Orders.Infrastructure` to
   `Catalog.Contracts`):

   ```csharp
   services.AddModuleClient<ICatalogGateway, CatalogHttpGateway>(configuration, CatalogModuleApi.Name);
   ```

(Every type name in these steps is illustrative.)

### What a module client does for you

`AddModuleClient<TPort, TImplementation>(configuration, module)` (`BuildingBlocks.Infrastructure`) registers a
typed `HttpClient` that:

| Concern | Behavior |
|---|---|
| Address | `Modules:<Module>:BaseAddress` if set; otherwise this process's own HTTP address (the module must then be enabled here). Resolved per request, so it works once the server is listening. |
| Authentication | Sends the internal API key (`X-Internal-Api-Key`) |
| Tracing | Sends the current `X-Correlation-ID`. W3C `traceparent` is propagated automatically, so one operation is one trace across modules. |
| Resilience | Standard pipeline from `Microsoft.Extensions.Http.Resilience`: per-attempt timeout (10 s), total timeout (30 s) and circuit breaker. **Retries only safe methods** (`GET`, `HEAD`, ...), so a command is never sent twice. |
| Failures | Unreachable, timed out or circuit open → `ModuleCallException` (`BuildingBlocks.Abstractions.Modules`) naming the module. Error status → `EnsureModuleSuccessAsync` throws `ModuleCallException` with the status and the problem details title/detail. |
| Startup check | The host refuses to start if a client's module is neither enabled here nor given a `BaseAddress`, or is remote without a shared key. The error says which setting to set. |

Calls go over real HTTP (loopback) even inside the monolith. That costs a little latency, but it means
serialization, status codes, timeouts and versioning behave exactly as they will after a split. Nothing about
a call changes when a module moves out.

### Designing internal APIs

- **Coarse-grained.** One call per use case, not one per field. Chatty APIs become slow once they cross the
  network.
- **Backward compatible.** Add fields; never rename or remove them. `<Module>.Contracts` is a published
  contract: once modules deploy separately, callers upgrade on their own schedule.
- **No distributed transactions.** A module call cannot join the caller's database transaction. Make commands
  idempotent (accept a client-generated ID), or replace the call with an integration event and eventual
  consistency.
- **Don't build call chains.** If A calls B, which calls C, then A depends on all of them being up. Prefer
  events, or let a module keep a local copy (a projection) of the data it reads often.

## 4. Internal API security

Every request under `/internal/` (including unknown paths, so modules cannot be probed) must carry the internal
API key in `X-Internal-Api-Key`. Otherwise it gets `403` with an empty body, and a warning is logged.

| `InternalApi:ApiKey` | Effect | Use |
|---|---|---|
| Not set (default) | A random key is generated at startup. Only this process knows it, so only the application itself can call its internal APIs. | The monolith: nothing to configure |
| Set (at least 32 characters) | Every process uses that key | Required as soon as modules run in more than one process; set the **same** key everywhere |

The key is a secret: set it with user secrets or an environment variable (`InternalApi__ApiKey`), never in
`appsettings*.json` **(enforced)**. Generate one with `openssl rand -hex 32`. The startup log says which mode is
active (`Internal API access: per-process key ...` or `shared key ...`) and never prints the key.

The key is the minimum safeguard. Still block `/internal/*` at the gateway / ingress, and in production
consider mutual TLS or a service mesh between processes.

## 5. Shared infrastructure inside one process

| Resource | In the monolith | Per module |
|---|---|---|
| PostgreSQL | One database | Own `DbContext`, connection string (`ConnectionStrings:<Module>Db`), table prefix, migrations history table and health check (`postgresql-<Module>Db`). See [database.md](database.md). |
| RabbitMQ | One connection and publisher for the process | Own queues, named `<module>.<purpose>`. See [messaging.md](messaging.md). |
| Kafka | One producer and admin client for the process | Own consumer group (`<Module>...StreamingServiceCollectionExtensions.ConsumerGroup` = the module name), passed to every `AddKafkaConsumer`. See [streaming.md](streaming.md). |
| MediatR, FluentValidation | One container | Handlers and validators of every enabled module; a module can only send the requests it can see (its own) |
| Logging | One Serilog pipeline; `Application` = `Host.Api` | `Module` property on every request to a module endpoint |
| Health | `/health/live`, `/health/ready` | `ready` runs the checks of every enabled module |

Because queues and consumer groups are already per module, extracting a module doesn't touch message routing
or Kafka offsets.

## 6. Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Modules:<Module>:Enabled` | `true` | Whether this process hosts the module: its endpoints, DbContext, consumers and health checks |
| `Modules:<Module>:BaseAddress` | empty | Where to call the module when it runs elsewhere, e.g. `http://catalog:8080`. Empty: call this process. |
| `InternalApi:ApiKey` | empty | Shared key for internal APIs across processes (section 4) |

Environment variables: `Modules__Catalog__Enabled`, `Modules__Catalog__BaseAddress`, `InternalApi__ApiKey`.

At startup the host logs what it runs:

```text
Modules hosted in this process: catalog, orders, notifications. Internal API access: per-process key (InternalApi:ApiKey not set)
```

Wrong settings stop the host with instructions: an invalid or duplicate module name, no module enabled, a
`BaseAddress` that isn't an absolute http(s) URL, a key shorter than 32 characters, or a module client whose
module is unreachable (section 3).

## 7. Extracting a module (example: Catalog)

No code changes. The same `Host.Api` image is deployed twice with different settings.

1. **Share the internal API key.** Set the same `InternalApi__ApiKey` on every deployment.
2. **Deploy the extracted module.** A new deployment of `Host.Api` that runs only Catalog:

   ```yaml
   catalog:
     image: dotnet10template/host-api
     environment:
       Modules__Orders__Enabled: "false"
       Modules__Notifications__Enabled: "false"
       ConnectionStrings__CatalogDb: ${CATALOG_DB}
       InternalApi__ApiKey: ${INTERNAL_API_KEY}
   ```

3. **Point the rest at it.** On the existing deployment, stop hosting Catalog and give its address:

   ```yaml
   app:
     image: dotnet10template/host-api
     environment:
       Modules__Catalog__Enabled: "false"
       Modules__Catalog__BaseAddress: http://catalog:8080
       InternalApi__ApiKey: ${INTERNAL_API_KEY}
   ```

   Orders' `CatalogHttpGateway` now calls `http://catalog:8080/internal/catalog/...` instead of the loopback
   address. Same paths, same contracts, same code.

4. **Route public traffic.** Send `/api/catalog/*` to the new deployment at the gateway.
5. **Optionally move the data.** Give Catalog its own database by copying `CAT_*` tables and changing only
   `ConnectionStrings__CatalogDb` ([database.md](database.md), section 8). This can be done before, during or
   after the process split.
6. **Messaging needs nothing.** Catalog's RabbitMQ queues (`catalog.*`) and Kafka consumer group (`catalog`)
   are the same, so it resumes where the monolith left off. Stop the old deployment's Catalog consumers first
   (step 3 does that), so a message isn't handled by both during the switch.

Later, if the module should become a separately built service, give it its own small host project that calls
`AddModules(new CatalogModule())`, and publish `Catalog.Contracts` as a NuGet package for its callers. The module
code still doesn't change.

`tests/BuildingBlocks.IntegrationTests/Modules/ModuleCommunicationTests` proves this flow: the same two modules
talk over HTTP in one host, then across two hosts configured as in steps 1–3.

## 8. Adding a module

1. Create `src/Modules/<Module>/` with `<Module>.Domain`, `.Application`, `.Infrastructure` and `.Api` (copy an
   existing module and rename), and `src/Contracts/<Module>.Contracts` with `<Module>ModuleApi.Name`. Name:
   lowercase letters, digits and hyphens. Add all five projects to `Dotnet10Template.slnx`.
2. Give it its own DbContext, connection string name `<Module>Db` and a unique table prefix
   ([database.md](database.md), section 9). Set its Kafka `ConsumerGroup` and RabbitMQ `QueuePrefix` to the
   module name.
3. Reference `<Module>.Api` from `Host.Api` and add `new <Module>Module()` to `AddModules` in `Program.cs`.
4. Add `Modules:<Module>` (`Enabled`, `BaseAddress`) and `ConnectionStrings:<Module>Db` to the host's
   `appsettings.json`.

`Architecture.Tests` fails until steps 1, 3 and 4 are complete **(enforced)**.

## 9. Rules enforced by `Architecture.Tests`

| Rule | Test |
|---|---|
| Exactly one host, referencing only `<Module>.Api` projects and `BuildingBlocks.Hosting/Web` | `ModuleRuleTests` |
| Every module has an Api project with `<Module>Module : IModule`, registered in `Program.cs` | `ModuleRuleTests` |
| Every module has `src/Contracts/<Module>.Contracts`. Contracts reference only `BuildingBlocks.Abstractions` and no packages. | `ModuleRuleTests` |
| No project reference to another module's projects; other modules' contracts only from Infrastructure | `DependencyRuleTests` |
| No source code naming another module's Domain/Application/Infrastructure/Api | `ModuleRuleTests` |
| One DbContext per module, in its Infrastructure, with a unique, applied table prefix | `PersistenceRuleTests` |
| Host settings configure every module (`Modules:<Module>`, `ConnectionStrings:<Module>Db`) with no secret values | `ConfigurationTests` |
