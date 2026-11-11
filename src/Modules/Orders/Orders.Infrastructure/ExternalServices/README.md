# External services

Clients for anything outside this module, implementing ports from `Application/Abstractions`:

- **Other modules**: a typed HTTP client of the other module's internal API, registered with
  `services.AddModuleClient<IPort, Implementation>(configuration, OtherModuleApi.Name)`. Request and response
  types come from `src/Contracts/<Other>.Contracts`; map them to this module's own types here, so the other
  module's contract never leaks into this module's Application or Domain. See docs/modules.md.
- **Third-party services**: HTTP and SDK clients, with timeouts and resilience.

Never reference another module's Domain, Application or Infrastructure project, and never read its tables.
