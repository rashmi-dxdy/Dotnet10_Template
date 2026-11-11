# .NET 10 Modular Monolith Template

A starting point for a **modular monolith** on .NET 10 LTS: one deployable application (`src/Host/Host.Api`)
hosting independent modules (Catalog, Orders, Notifications). Each module is a DDD/CQRS bounded context with its
own tables (prefixed, in one shared database), and modules talk only through internal HTTP APIs and integration
events. Any module can later be **extracted to its own process and database by configuration, without code
changes** (see [docs/modules.md](docs/modules.md)).

It contains **no business functionality**: only the structure, conventions, build rules and infrastructure that
features plug into.

> **Status:** the module skeletons, host, infrastructure wiring and the Docker Compose development
> environment are in place.

## Technology stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 (LTS), C# |
| Web APIs | ASP.NET Core Web API, OpenAPI |
| Data access | Entity Framework Core 10, PostgreSQL (Npgsql provider) |
| Logging | Serilog (structured logging) |
| Messaging | RabbitMQ (`RabbitMQ.Client`), Apache Kafka (`Confluent.Kafka`) |
| Local infrastructure | Docker, Docker Compose |

## Prerequisites

- [.NET SDK 10.0.403](https://dotnet.microsoft.com/download/dotnet/10.0) or a later 10.0.4xx feature band (see `global.json`)
- Docker Desktop with Docker Compose v2
- Git

## Repository layout

```text
.
├── src/
│   ├── Host/Host.Api          # The one deployable application: composes the modules (startup project)
│   ├── Modules/<Module>/      # <Module>.Domain, .Application, .Infrastructure, .Api
│   ├── Contracts/             # IntegrationEvents + <Module>.Contracts (what other modules may see)
│   └── BuildingBlocks/        # Shared technical plumbing, no business code
├── tests/                     # Architecture, unit and integration tests
├── Dotnet10Template.slnx      # Solution (XML .slnx format)
├── global.json                # Pins the .NET SDK version
├── Directory.Build.props      # Shared MSBuild settings for every project
├── Directory.Packages.props   # Central package versions (NuGet CPM)
├── .editorconfig              # Formatting, code-style and naming rules
├── .env.example               # Template for local environment variables
└── ...
```

## Documentation

- [docs/modules.md](docs/modules.md): modular monolith, module anatomy, calling another module, internal API security, extracting a module
- [docs/architecture.md](docs/architecture.md): DDD boundaries, CQRS, dependency rules, events, cross-module communication
- [docs/code-quality.md](docs/code-quality.md): analyzers, formatting and how to fix violations
- [Docker](#docker) (below): Docker Compose development environment (app, PostgreSQL, RabbitMQ, Kafka)
- [docs/logging.md](docs/logging.md): Serilog structured logging, configuration layering, Docker overrides, secrets
- [docs/database.md](docs/database.md): EF Core + PostgreSQL, shared database with per-module table prefixes, connection strings, health checks, migrations
- [docs/messaging.md](docs/messaging.md): RabbitMQ publishing and consuming, ack/nack/requeue, dead-lettering, reconnection, smoke tests
- [docs/streaming.md](docs/streaming.md): Kafka producing and consuming, topics, consumer groups, offsets, dead-letter topics, smoke tests

## Getting started

```bash
# Verify the SDK selected by global.json
dotnet --version

# Full quality gate: restore, format check, build (warnings as errors), tests
./scripts/verify.sh            # PowerShell: ./scripts/verify.ps1

# Fix formatting and auto-fixable analyzer issues
./scripts/format.sh            # PowerShell: ./scripts/format.ps1

# Create your local environment file (never commit .env)
cp .env.example .env

# EITHER run everything (app + PostgreSQL + RabbitMQ + Kafka) in Docker - see "Docker" below
docker compose up --build

# OR run the application (every module) on your machine, on http://localhost:5100
# Needs ConnectionStrings:<Module>Db and RabbitMq credentials in user secrets - see docs/database.md, docs/messaging.md
dotnet run --project src/Host/Host.Api
```

In Visual Studio or Rider, set **Host.Api** as the startup project.

## Docker

`docker compose up --build` starts the whole development environment with one command:

| Container | Image | Purpose | Published to your machine |
|---|---|---|---|
| `app` | `docker/Dockerfile.dev` (.NET 10 SDK) | `Host.Api` running **all modules** (Catalog, Orders, Notifications) under `dotnet watch` | `localhost:${APP_PORT}` (8080) |
| `postgres` | `postgres:17-alpine` | The shared database (each module owns its prefixed tables) | nothing |
| `rabbitmq` | `rabbitmq:4-management-alpine` | Integration events | management UI `localhost:15672` |
| `kafka` | `apache/kafka:3.9.0` | Event streams; single broker, KRaft (no ZooKeeper) | `localhost:9094` (EXTERNAL listener) |

The app reaches the others by service name (`postgres`, `rabbitmq`, `kafka:9092`) and starts only when all three
are healthy. Published ports bind to `127.0.0.1` only. PostgreSQL (5432), AMQP (5672) and Kafka's internal
listener (9092) are not published.

### First-time setup

```bash
cp .env.example .env        # then edit .env:
#  - replace every change-me password
#  - KAFKA_CLUSTER_ID: generate ONCE and never change it
#      docker run --rm apache/kafka:3.9.0 /opt/kafka/bin/kafka-storage.sh random-uuid
#  - INTERNAL_API_KEY (recommended, at least 32 characters): openssl rand -hex 32
```

`.env` is git-ignored; `docker-compose.yml` contains no credentials and fails fast if a required value is missing.
`docker/postgres/init.sh` creates the app database and login from `APP_DB_*` on the **first** start of an empty
database volume.

### Everyday commands

| Task | Command |
|---|---|
| Start everything (foreground, logs in the terminal) | `docker compose up --build` |
| Start in the background | `docker compose up --build -d` |
| Container status and health | `docker compose ps` |
| View logs (all / app only, follow) | `docker compose logs -f` / `docker compose logs -f app` |
| Stop everything (containers removed, **data kept**) | `docker compose down` |
| Stop without removing containers | `docker compose stop` |
| Restart one container | `docker compose restart postgres` |
| Rebuild the app image | `docker compose build --no-cache app` then `docker compose up -d` |

**Never use `docker compose down -v` in the normal workflow**: `-v` deletes the data volumes.

### Live reload

The repository is bind-mounted into the container at `/workspace` (`docker-compose.override.yml`) and the
container runs `dotnet watch`. Save a `.cs` file and the change is hot-reloaded, or the app restarts
automatically when hot reload cannot apply it (`DOTNET_WATCH_RESTART_ON_RUDE_EDIT`). `.csproj` changes trigger a
restore and restart the same way. File changes are detected by polling (`DOTNET_USE_POLLING_FILE_WATCHER`)
because Docker Desktop on Windows/macOS does not forward file-system events into containers; expect a second or
two of delay.

**Rebuild the image** (`docker compose up --build`, or the rebuild command above) only after changing
`docker/Dockerfile.dev` or `docker/watch.sh`. Source and project changes never need it. A rebuild is also harmless if in doubt.

Build output goes to `/build` inside the container, not to `bin/` and `obj/` in your checkout, so the Docker build
and your Visual Studio / Rider builds never overwrite each other. NuGet packages are cached in the `nuget_cache`
volume. The first start restores every package and builds the whole solution graph (a few minutes with an empty
cache); later starts take about a minute.

`docker compose stop` / `down` shut the app down gracefully (`HostOptions__ShutdownTimeout`, 30 s).
`dotnet watch` itself ignores the container's stop signal, so the entrypoint `docker/watch.sh` passes it to the
app first.

### Endpoints

| Endpoint | URL |
|---|---|
| OpenAPI | <http://localhost:8080/openapi/v1.json> |
| Liveness | <http://localhost:8080/health/live> |
| Readiness (PostgreSQL, RabbitMQ, Kafka) | <http://localhost:8080/health/ready> |
| Catalog | `http://localhost:8080/api/catalog/...` |
| Orders | `http://localhost:8080/api/orders/...` |
| Notifications | `http://localhost:8080/api/notifications/...` |
| RabbitMQ management UI | <http://localhost:15672> (log in with `RABBITMQ_USER` / `RABBITMQ_PASSWORD`) |
| Kafka, for tools on your machine | `localhost:9094` |

The module routes contain no endpoints yet (no business functionality), so they return 404 until you add some.
`/internal/<module>/...` is for module-to-module calls only and returns 403 without the internal API key.

### Data persistence and resetting it on purpose

PostgreSQL, RabbitMQ and Kafka keep their data in the named volumes `postgres_data`, `rabbitmq_data` and
`kafka_data`. Data survives `docker compose restart`, `stop`, `down` and image rebuilds.

To reset data **intentionally**, stop the stack and delete only the volume you mean to reset (Compose prefixes the
names with the project name `dotnet10template`):

```bash
# Database: deletes ALL tables and data. init.sh recreates the app database and login on the next start.
docker compose down
docker volume rm dotnet10template_postgres_data

# Messaging: deletes all RabbitMQ queues/messages and all Kafka topics, messages and consumer offsets.
docker compose down
docker volume rm dotnet10template_rabbitmq_data dotnet10template_kafka_data

docker compose up -d
```

Reset the database volume after changing `APP_DB_*` or `POSTGRES_*` (they are applied only on first start), and
the RabbitMQ volume after changing `RABBITMQ_USER` / `RABBITMQ_PASSWORD`. Changing `KAFKA_CLUSTER_ID` requires
deleting `kafka_data`, otherwise Kafka refuses to start. `docker volume rm dotnet10template_nuget_cache` clears the
package cache (harmless; packages are downloaded again).

## Conventions

### Package management

The repo uses NuGet [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management).
All versions live in `Directory.Packages.props`. Project files reference packages **without** a version:

```xml
<PackageReference Include="Serilog.AspNetCore" />
```

To upgrade a package, change its version in `Directory.Packages.props` only.

### Build settings

`Directory.Build.props` applies these settings to every project:

- `net10.0`, nullable reference types and implicit usings
- `AnalysisLevel=latest-recommended`, all compiler warning waves, with code style enforced during the build
- Meziantou, SonarAnalyzer and Roslynator analyzers in every project (see [docs/code-quality.md](docs/code-quality.md))
- Warnings treated as errors. Low/moderate NuGet vulnerability advisories (NU1901/NU1902) stay as warnings.
- Deterministic builds. `ContinuousIntegrationBuild` is turned on automatically when `CI`, `GITHUB_ACTIONS` or `TF_BUILD` is set.

### Code style

`.editorconfig` enforces file-scoped namespaces, explicit accessibility modifiers, `_camelCase` private fields,
PascalCase types and members, `I`-prefixed interfaces and `T`-prefixed type parameters.
It also sets every analyzer severity. [docs/code-quality.md](docs/code-quality.md) explains which tool owns which rules
and **how to fix analyzer violations**.
Run `dotnet format` to apply the rules.

### Line endings

`.gitattributes` normalises text files to LF. Only `.cmd`/`.bat` scripts use CRLF.

## Secrets

- Never commit `.env`, `secrets.json`, certificates or real connection strings. All of these are git-ignored.
- `.env.example` contains placeholders only.
- For local development, prefer `dotnet user-secrets` (on `Host.Api`) or environment variables.
