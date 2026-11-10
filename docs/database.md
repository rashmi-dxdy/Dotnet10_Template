# Database: EF Core 10 and PostgreSQL

Every service uses EF Core 10 with the Npgsql provider. The setup is central (one helper in
`BuildingBlocks.Infrastructure`), but every service has **its own DbContext, its own connection string and its
own table prefix**. The template contains no entities, tables, migrations or seed data.

## 1. Database ownership model

### Current phase: one shared database, tables prefixed per service

For now all services use **one PostgreSQL database**. Each service owns the tables that carry its prefix:

| Service | DbContext | Table prefix | Example table | Migrations history table |
|---|---|---|---|---|
| Catalog | `CatalogDbContext` | `CAT_` | `CAT_Products` | `CAT_EFMigrationsHistory` |
| Orders | `OrdersDbContext` | `ORD_` | `ORD_Orders` | `ORD_EFMigrationsHistory` |
| Notifications | `NotificationsDbContext` | `NTF_` | `NTF_Messages` | `NTF_EFMigrationsHistory` |

```text
┌──────────────────────── one PostgreSQL database ────────────────────────┐
│  CAT_Products  CAT_...        ORD_Orders  ORD_...       NTF_Messages ... │
│  CAT_EFMigrationsHistory      ORD_EFMigrationsHistory   NTF_EFMigr...    │
└────────▲──────────────────────────────▲──────────────────────▲──────────┘
     Catalog.Api                    Orders.Api        Notifications.Worker
 (CatalogDbContext, CAT_)       (OrdersDbContext, ORD_)  (NotificationsDbContext, NTF_)
```

The prefix is applied automatically to every table of the service's model (entity tables, explicit
`ToTable("...")` names, many-to-many join tables, owned types, inheritance hierarchies) by
`modelBuilder.ApplyTablePrefix(TablePrefix)` at the end of each `OnModelCreating`. Write entity configurations
with plain names (`ToTable("Products")`); the prefix is added for you.

### Ownership rules (they keep the later split cheap)

Sharing a database does **not** mean sharing data. Treat each prefix as if it were already a separate database:

| Rule | Why |
|---|---|
| A service reads and writes **only its own prefix**, through its own DbContext | Another service's tables are its private implementation |
| **No foreign keys, joins, views or SQL across prefixes** | They would break the moment the tables move to separate databases |
| Reference another service's data **by ID only**, and get it through integration events or an API | Each service keeps the copy it needs |
| One DbContext per service, declared only in its own `Infrastructure` **(enforced)** | No shared model, no shared tables |
| Every DbContext declares a unique `TablePrefix` and applies it **(enforced)** | No name clashes in the shared database |
| Each service has its own connection string key and migrations history table | Moving a service out is a configuration change, and migration records never mix |
| Migrations touch **only** their own service's tables | Services deploy independently |

### Later: one database per service

When a service needs its own database (scale, isolation, independent backups or upgrades), it moves out
without code changes. See section 8. In production, separate databases (ideally separate servers or managed
instances) are the target. The shared database is a starting point, not the end state.

## 2. What is in the template

| Where | What |
|---|---|
| `BuildingBlocks.Infrastructure/DependencyInjection/PersistenceServiceCollectionExtensions.cs` | `AddPostgresDbContext<TContext>(configuration, connectionStringName, tablePrefix)`: the central EF Core + Npgsql setup |
| `BuildingBlocks.Infrastructure/Persistence/TablePrefix.cs` | `ApplyTablePrefix` (model-wide prefixing), prefix validation, per-service history table name |
| `BuildingBlocks.Infrastructure/Persistence/` | Connection string validation, shared `Database` options, PostgreSQL health check, startup connectivity check |
| `<Service>.Infrastructure/Persistence/<Service>DbContext.cs` | The service's own DbContext (no entities yet), with `ConnectionStringName` and `TablePrefix` |
| `<Service>.Infrastructure/DependencyInjection/PersistenceServiceCollectionExtensions.cs` | `Add<Service>Persistence(configuration)`: the service's extension point, called by `Add<Service>Infrastructure` |

`AddPostgresDbContext` is generic on purpose. It configures a context; it never defines one. There is no
shared DbContext base class.

### Where EF Core may be referenced **(enforced)**

| Package | Allowed in |
|---|---|
| `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` | `<Service>.Infrastructure`, `BuildingBlocks.Infrastructure` |
| `Microsoft.EntityFrameworkCore.Design`, `.Tools` | Host projects (`Api`, `Worker`) only, as `PrivateAssets="all"`: design-time tooling for migrations |
| none of the above | Domain, Application, Contracts, other BuildingBlocks |

Domain and Application never see EF Core. They define repository interfaces (`Domain/Repositories`) that
Infrastructure implements.

## 3. Configuration

### Connection strings

Each service has its own key. **For now all three contain the same connection string** (the shared database):

| Service | Key | Environment variable |
|---|---|---|
| Catalog.Api | `ConnectionStrings:CatalogDb` | `ConnectionStrings__CatalogDb` |
| Orders.Api | `ConnectionStrings:OrdersDb` | `ConnectionStrings__OrdersDb` |
| Notifications.Worker | `ConnectionStrings:NotificationsDb` | `ConnectionStrings__NotificationsDb` |

Format: `Host=...;Port=5432;Database=...;Username=...;Password=...` ([Npgsql keywords](https://www.npgsql.org/doc/connection-string-parameters.html)).

**Credentials are read only from user secrets (development) or environment variables (Docker, CI,
production).** The committed `appsettings.json` contains an empty placeholder that documents the key.
`Architecture.Tests` fails if any committed connection string, password, secret or token has a value
**(enforced)**.

Development: set it once per host project, from the project folder, or right-click the host project >
*Manage User Secrets* in Visual Studio / *Tools > .NET User Secrets* in Rider:

```bash
cd src/Services/Catalog/Catalog.Api
dotnet user-secrets set "ConnectionStrings:CatalogDb" "Host=localhost;Port=5432;Database=dotnet10template;Username=app;Password=<your local password>"
# same value for Orders.Api (OrdersDb) and Notifications.Worker (NotificationsDb)
```

Docker Compose (values from the git-ignored `.env`, see `.env.example`):

```yaml
x-app-db: &app-db "Host=postgres;Port=5432;Database=${APP_DB_NAME};Username=${APP_DB_USER};Password=${APP_DB_PASSWORD}"

catalog-api:
  environment:
    ConnectionStrings__CatalogDb: *app-db
orders-api:
  environment:
    ConnectionStrings__OrdersDb: *app-db
notifications-worker:
  environment:
    ConnectionStrings__NotificationsDb: *app-db
```

At startup the connection string is validated:

- **Missing** → the service stops immediately (exit code 1) with a `Fatal` log entry that explains how to
  set it.
- **Invalid** or without `Host`/`Database` → the same, without ever repeating the configured value (it may
  contain a password).
- `Application Name` defaults to the service name, so `pg_stat_activity` shows which service holds which
  connection. That's handy while they share a database.

### Shared EF Core settings: `Database` section

| Setting | Default | Meaning |
|---|---|---|
| `CommandTimeoutSeconds` | `30` | Command timeout |
| `MaxRetryCount` | `5` | Retries on transient failures (Npgsql execution strategy). `0` disables retries |
| `MaxRetryDelaySeconds` | `10` | Maximum delay between retries |
| `ConnectivityProbeTimeoutSeconds` | `5` | How long the startup check and health check wait for a connection |
| `EnableDetailedErrors` | `false` (`true` in Development) | More detailed EF Core errors |
| `EnableSensitiveDataLogging` | `false` | Logs parameter values and entity data. **Keep it off**: they can contain personal or secret data |

Invalid values stop the service at startup (`ValidateOnStart`). Override any of them with environment
variables, e.g. `Database__MaxRetryCount=10`.

With retries enabled, wrap user-initiated transactions in the execution strategy
(`context.Database.CreateExecutionStrategy().ExecuteAsync(...)`). EF Core throws a clear error otherwise.

## 4. When PostgreSQL is unavailable

The service **starts anyway**, because PostgreSQL may just be starting too (e.g. in Docker Compose).

1. A startup check connects once and logs the outcome:

   ```text
   [ERR] PostgreSQL CatalogDb is unavailable at app@localhost:5432/dotnet10template: cannot reach the server
         (No connection could be made because the target machine actively refused it). Is PostgreSQL running
         (e.g. `docker compose up -d postgres`) and are Host and Port correct? The service keeps running, but
         database operations will fail and /health/ready reports Unhealthy until PostgreSQL is reachable.
         Check ConnectionStrings:CatalogDb (user secrets or environment variable). See docs/database.md
   ```

   It only ever shows `user@host:port/database`, never the password. On success it logs
   `PostgreSQL CatalogDb is reachable at ...`.

2. `/health/ready` returns `503 Unhealthy` until the database is reachable. `/health/live` stays `200`, so an
   orchestrator stops sending traffic but does not restart the container.

3. Database operations retry transient failures (`Database:MaxRetryCount`) and then fail.

| Message says | Cause | Fix |
|---|---|---|
| `cannot reach the server (...refused...)` | Nothing listening at Host:Port | Start PostgreSQL; check `Host`/`Port` (`postgres` inside Docker, `localhost` outside) |
| `cannot reach the server (Timeout ...)` / `no response within N s` | Firewall, wrong host, server overloaded | Check network and host name |
| `authentication failed` | Wrong `Username`/`Password` | Fix the user secret / environment variable |
| `the database does not exist` | The database was not created | Create it (section 6) or fix `Database=` |
| `'ConnectionStrings:...' is not configured` (`Fatal`, exit code 1) | No connection string at all | Set it (section 3) |

## 5. Health checks

| Endpoint | Runs | Use for |
|---|---|---|
| `/health/live` | No checks | Liveness: the process is up |
| `/health/ready` | Checks tagged `ready`, including `postgresql-<Name>Db` | Readiness: the service can do its work |

Responses contain only the status (`Healthy` / `Unhealthy`), no details. Details, with the target but never the
password, go to the logs. The Worker has no HTTP endpoints. Its PostgreSQL check is registered and can be
exposed later (e.g. a health check publisher). Its startup check logs the same messages.

The health check opens a plain connection (no EF Core retries), so it answers within
`ConnectivityProbeTimeoutSeconds`. Give your readiness probe a longer timeout than that.

## 6. Local development database

Run one PostgreSQL container, then create the shared application database and login, using the values from
your `.env`. The template does not do this for you, because it creates no databases. With `psql` as the
administrator:

```sql
CREATE ROLE app LOGIN PASSWORD '<APP_DB_PASSWORD>';
CREATE DATABASE dotnet10template OWNER app;
```

The services create their own prefixed tables through migrations (section 7).

## 7. Migrations (when the first entities exist)

The template has none. Each service has its **own** migrations, in its own
`Infrastructure/Persistence/Migrations`, recorded in its **own** history table (`CAT_EFMigrationsHistory`, ...).
A migration creates or changes only that service's prefixed tables. The host project is the startup project,
because it holds the configuration and the EF design-time packages.

**CLI / Rider / VS Code** (`dotnet ef` is in the local tool manifest; run `dotnet tool restore` once):

```bash
dotnet ef migrations add InitialCreate \
  --project src/Services/Catalog/Catalog.Infrastructure \
  --startup-project src/Services/Catalog/Catalog.Api \
  --output-dir Persistence/Migrations
```

**Visual Studio, Package Manager Console:**

```powershell
Add-Migration InitialCreate -Project Catalog.Infrastructure -StartupProject Catalog.Api -OutputDir Persistence/Migrations
```

Both need the service's connection string configured (section 3). `migrations add` does not connect;
`database update` does. Check the generated migration: every table name must start with the service's prefix.

Applying migrations: do it as a deployment step (`dotnet ef migrations bundle` or
`dotnet ef migrations script --idempotent`), not automatically at service startup. With several replicas,
startup migrations race each other, and the service login should not need DDL rights in production.

**Never change a service's `TablePrefix` once its tables exist.** It renames every table.

## 8. Moving a service to its own database later

Because of the rules in section 1, this needs **no code changes**. For example, for Orders:

1. Create the new database (and ideally a dedicated login for Orders).
2. Copy the Orders tables, **only** `ORD_*` including `ORD_EFMigrationsHistory`, e.g. with
   `pg_dump --table='"ORD_*"' ... | psql <new database>`, during a maintenance window or with replication.
3. Point **only** `ConnectionStrings__OrdersDb` at the new database and redeploy Orders.
4. Verify, then drop the `ORD_*` tables from the shared database.

The prefix can stay. It is harmless in a dedicated database, and keeping it avoids a rename migration.

## 9. Adding persistence to a new service

1. Create `<Service>.Infrastructure/Persistence/<Service>DbContext.cs` (copy an existing one) with
   `ConnectionStringName = "<Service>Db"` and a new, unique `TablePrefix` (2-10 upper-case letters or digits
   plus `_`, e.g. `INV_`). Keep `modelBuilder.ApplyTablePrefix(TablePrefix)` as the last line of
   `OnModelCreating`.
2. Create `Add<Service>Persistence` in `<Service>.Infrastructure/DependencyInjection/` calling
   `AddPostgresDbContext<<Service>DbContext>(configuration, <Service>DbContext.ConnectionStringName,
   <Service>DbContext.TablePrefix)`, and call it from `Add<Service>Infrastructure`.
3. Add `"ConnectionStrings": { "<Service>Db": "" }` and the `Database` section to the host's
   `appsettings.json`, and set the real value (the shared database) in user secrets / environment.
4. Reference `Microsoft.EntityFrameworkCore.Design` and `.Tools` (`PrivateAssets="all"`) from the host.
5. Add the prefix to the table in section 1.
