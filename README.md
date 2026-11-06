# .NET 10 Microservices Template

A starting point for building microservices on .NET 10 LTS. It provides the shared repository structure,
build configuration and conventions. It contains **no business functionality**, so each service you add
gets the same build rules, analyzers and package versions.

> **Status:** the solution skeleton only. Service projects and the Docker Compose infrastructure are not added yet.

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
├── Dotnet10Template.slnx      # Solution (XML .slnx format)
├── global.json                # Pins the .NET SDK version
├── Directory.Build.props      # Shared MSBuild settings for every project
├── Directory.Packages.props   # Central package versions (NuGet CPM)
├── .editorconfig              # Formatting, code-style and naming rules
├── .env.example               # Template for local environment variables
└── ...
```

## Getting started

```bash
# Verify the SDK selected by global.json
dotnet --version

# Restore and build
dotnet build Dotnet10Template.slnx

# Create your local environment file (never commit .env)
cp .env.example .env
```

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
- `AnalysisLevel=latest-recommended`, with code style enforced during the build
- Warnings treated as errors. Low/moderate NuGet vulnerability advisories (NU1901/NU1902) stay as warnings.
- Deterministic builds. `ContinuousIntegrationBuild` is turned on automatically when `CI`, `GITHUB_ACTIONS` or `TF_BUILD` is set.

### Code style

`.editorconfig` enforces file-scoped namespaces, explicit accessibility modifiers, `_camelCase` private fields,
PascalCase types and members, `I`-prefixed interfaces and `T`-prefixed type parameters.
Run `dotnet format` to apply the rules.

### Line endings

`.gitattributes` normalises text files to LF. Only `.cmd`/`.bat` scripts use CRLF.

## Secrets

- Never commit `.env`, `secrets.json`, certificates or real connection strings. All of these are git-ignored.
- `.env.example` contains placeholders only.
- For local development, prefer `dotnet user-secrets` or environment variables.
