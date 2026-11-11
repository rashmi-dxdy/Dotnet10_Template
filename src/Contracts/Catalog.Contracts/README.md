# Catalog.Contracts

The Catalog module's contract with **other modules**. Put here only what callers need to talk to its internal API:

- request and response DTOs (`sealed record`s) of the endpoints under `/internal/catalog/...`;
- route templates, as constants, if callers build paths.

Rules (enforced by `Architecture.Tests`):

- no references to `Catalog.Domain`, `Catalog.Application` or `Catalog.Infrastructure`, and no packages;
- never expose domain types: map them to DTOs in the module's Application layer;
- change it backward compatibly (add fields, never rename or remove them): callers deploy independently
  once modules are split.

Integration events (asynchronous) stay in `src/Contracts/IntegrationEvents`.
