# Logging and configuration

Every service (Catalog.Api, Orders.Api, Notifications.Worker) uses the same Serilog setup, from
`BuildingBlocks.Hosting` (all hosts) and `BuildingBlocks.Web` (HTTP APIs only). Nothing here is
IDE-specific: it works the same in Visual Studio, Rider, VS Code, `dotnet run` and Docker.

## 1. Configuration layering

Both `WebApplication.CreateBuilder` and `Host.CreateApplicationBuilder` read configuration in this order.
Later sources override earlier ones:

| # | Source | Use it for |
|---|---|---|
| 1 | `appsettings.json` | Safe defaults for every environment. Committed. **No secrets.** |
| 2 | `appsettings.{Environment}.json` | Environment-specific values. `appsettings.Development.json` holds development-only settings. Committed. **No secrets.** |
| 3 | User secrets (Development only) | Local secrets. `dotnet user-secrets set "Key" "value"` from the host project folder, or right-click the project > *Manage User Secrets* in Visual Studio / *Tools > .NET User Secrets* in Rider. |
| 4 | Environment variables | Docker, CI and production overrides. `__` (double underscore) separates sections. |
| 5 | Command-line arguments | One-off overrides, e.g. `--Serilog:MinimumLevel:Default=Debug`. |

The environment comes from `ASPNETCORE_ENVIRONMENT` (APIs) or `DOTNET_ENVIRONMENT` (Worker). The
`Properties/launchSettings.json` profiles set `Development`; anything else (e.g. Docker) defaults to
`Production`.

`Architecture.Tests` enforces **(enforced)**:

- every host has `appsettings.json` and `appsettings.Development.json`;
- the `Serilog` section is identical in every host, per file, so all services log the same way;
- no `Logging` section (Serilog replaces Microsoft.Extensions.Logging filtering, so it would be ignored);
- no committed value under a key that looks secret (`*Password*`, `*Secret*`, `*Token*`, `*ConnectionString*`,
  `*ApiKey*`, ...). Name the key with an empty value if you want to document it.

## 2. Log output

| Environment | Format | Level |
|---|---|---|
| `Development` (`appsettings.Development.json`) | Human-readable text with colours | `Debug` |
| Everything else (`appsettings.json`) | One JSON object per line (Docker, log collectors) | `Information` |

`Microsoft.*` and `System.*` are limited to `Warning`, except `Microsoft.Hosting.Lifetime` (startup and
shutdown) at `Information`.

Development:

```text
[18:46:56.851 INF] Catalog.Api Serilog.AspNetCore.RequestLoggingMiddleware corr=9c1a2dd0... trace=f00edd61...
      HTTP GET /products responded 404 in 0.1341 ms
```

JSON, using the [CLEF](https://clef-json.org) property names so tools such as Seq, Grafana Loki, Elastic and
`jq` read it directly:

```json
{"@t":"2026-11-09T13:19:18.08Z","@l":"Information","@m":"HTTP GET /products responded 400 in 5.5350 ms",
 "@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
 "@tr":"53ff7c31aa015305a4fe86ce7184c657","@sp":"97d8acfa82a47533","RequestMethod":"GET",
 "RequestPath":"/products","StatusCode":400,"Elapsed":5.535,"CorrelationId":"86c953ee...",
 "SourceContext":"Serilog.AspNetCore.RequestLoggingMiddleware","Application":"Catalog.Api",
 "Environment":"Production","MachineName":"web-1","ThreadId":10}
```

Both formats are `Serilog.Expressions` templates set in `Serilog:WriteTo:Console:Args:formatter`. The JSON
template is also in `LogFormats.Json`, used by the bootstrap logger. Keep them identical.

## 3. Standard log properties

| Property | Source | Present |
|---|---|---|
| `Application` | Host `ApplicationName` (e.g. `Catalog.Api`). Override with `Serilog:Properties:Application`. | Always |
| `Environment` | Host environment name | Always |
| `MachineName` | `Serilog.Enrichers.Environment` (the container ID in Docker) | Always |
| `ThreadId` | `Serilog.Enrichers.Thread` | Always |
| `SourceContext` | The logger category (class name) | Always |
| `CorrelationId` | `X-Correlation-ID` request header, or a new ID | HTTP requests; push it yourself in message consumers |
| `@tr` / `@sp` | W3C trace and span ID of the current `Activity` | HTTP requests, and anything running inside an `Activity` |

These are added in code (`AddServiceLogging`), not in configuration, so no service can drop them by accident.
Use the constants in `LogProperties` for the names.

### Correlation ID

`CorrelationIdMiddleware` (HTTP APIs):

- reuses an incoming `X-Correlation-ID` if it is 1–64 characters of `A-Z a-z 0-9 - _ . :`, otherwise
  generates a new one (this blocks log injection through the header);
- returns it in the `X-Correlation-ID` response header and in error responses (`correlationId`);
- adds it to every log entry written during the request;
- tags it on the trace activity as `correlation.id`.

Read it in code with `HttpContext.GetCorrelationId()`. In a message consumer (Worker), take the ID from
the message and push it for the duration of the handling:

```csharp
using (LogContext.PushProperty(LogProperties.CorrelationId, message.CorrelationId))
{
    await sender.Send(command, cancellationToken);
}
```

## 4. HTTP APIs

`builder.AddWebServiceDefaults()` and `app.UseWebServiceDefaults()` add, in this order:

1. **Correlation ID** (see above).
2. **Request logging** (`UseSerilogRequestLogging`): one entry per request with method, path, status code,
   duration, scheme, host and endpoint name. Level: `Error` for 5xx and exceptions, `Verbose` for `/health/*`
   (hidden by default), `Information` otherwise. Query strings, headers and bodies are **not** logged.
3. **Exception handling** (`UseExceptionHandler` + problem details): an unhandled exception is logged once
   at `Error` with its stack trace, correlation ID and trace ID. The client gets an RFC 9457 `500` response
   with `traceId` and `correlationId` but **no exception details**. A `FluentValidation.ValidationException`
   becomes a `400` with the field errors and is not logged as an error.

`UseWebServiceDefaults()` must be the first call after `Build()`.

## 5. Startup failures and graceful shutdown

`ServiceHost.RunAsync` wraps every `Program.cs`:

- A **bootstrap logger** (JSON, console) is active until configuration has been read, so even a broken
  `appsettings.json` is logged.
- If startup or the host fails, the exception is logged at `Fatal` and the process exits with code `1`.
- Logs are **flushed** on exit, so the last entries are never lost.

`HostLifetimeLogger` logs the shutdown:

```text
Graceful shutdown started; waiting up to 00:00:30 for in-flight work to finish
Application is shutting down...
Graceful shutdown completed after 01:02:03.456 uptime
```

`HostOptions:ShutdownTimeout` (default `00:00:30`) is how long hosted services and in-flight requests get
to finish after `SIGTERM` / Ctrl+C. **Docker kills a container 10 s after `docker stop` by default.** Set
`stop_grace_period` above the shutdown timeout, or lower the timeout.

## 6. Sensitive data

Rules for all code:

- Never log passwords, tokens, API keys, connection strings, request/response bodies, configuration sections
  or whole options objects.
- Log identifiers (`OrderId`), not personal data (names, e-mail addresses), unless there is a clear need.
- Don't enable EF Core `EnableSensitiveDataLogging()` outside local development.

Safety nets already in place:

- `SensitiveDataRedactionEnricher` replaces the value of any property whose name contains `password`,
  `secret`, `token`, `apikey`, `connectionstring`, `authorization`, `cookie`, `credential`, `privatekey`
  (and variants) with `***REDACTED***`. This includes properties inside destructured objects, dictionaries
  and sequences, and the rendered message. It cannot catch a secret logged under an innocent name or inside
  free text, so the rules above still apply.
- Request logging excludes query strings, headers and bodies.
- Error responses never contain exception details.
- The settings tests reject committed secret values.

## 7. Docker and environment variables

Every setting can be overridden with an environment variable: replace `:` with `__`.

| Setting | Environment variable |
|---|---|
| `Serilog:MinimumLevel:Default` | `Serilog__MinimumLevel__Default=Debug` |
| `Serilog:MinimumLevel:Override:Microsoft.AspNetCore` | `Serilog__MinimumLevel__Override__Microsoft.AspNetCore=Information` |
| `Serilog:Properties:Application` | `Serilog__Properties__Application=catalog-api` |
| `HostOptions:ShutdownTimeout` | `HostOptions__ShutdownTimeout=00:00:20` |
| `ConnectionStrings:CatalogDb` | `ConnectionStrings__CatalogDb=Host=postgres;...` |

Example Docker Compose service (the compose file itself is added with the Docker infrastructure):

```yaml
catalog-api:
  environment:
    ASPNETCORE_ENVIRONMENT: Production          # JSON logs
    Serilog__MinimumLevel__Default: ${SERILOG_MINIMUM_LEVEL:-Information}
    HostOptions__ShutdownTimeout: "00:00:20"
    ConnectionStrings__CatalogDb: "Host=postgres;Database=${APP_DB_NAME};Username=${APP_DB_USER};Password=${APP_DB_PASSWORD}"
  stop_grace_period: 30s                        # longer than HostOptions:ShutdownTimeout
```

Values such as `APP_DB_PASSWORD` come from the git-ignored `.env` file (see `.env.example`), never from
`appsettings*.json`.

## 8. Adding a new service

1. Reference `BuildingBlocks.Hosting` (and `BuildingBlocks.Web` for an HTTP API) from the host project.
2. Wrap `Program.cs` in `ServiceHost.RunAsync` and call `AddServiceDefaults()` (Worker) or
   `AddWebServiceDefaults()` + `UseWebServiceDefaults()` (API), as in the existing services.
3. Copy `appsettings.json` and `appsettings.Development.json` from an existing host. The `Serilog` section
   must stay identical (enforced).
4. Add a `<UserSecretsId>` to the host project.
