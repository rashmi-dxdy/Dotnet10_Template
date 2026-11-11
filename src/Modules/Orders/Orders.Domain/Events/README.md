# Domain events

Facts raised by aggregates, implementing `BuildingBlocks.Abstractions.Domain.IDomainEvent`.

- Handled in-process, inside this module only.
- Never published to other modules directly. To notify other modules, map a domain event to an
  integration event (`src/Contracts/IntegrationEvents`) in the Application layer.
