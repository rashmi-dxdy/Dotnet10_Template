# Domain events

Facts raised by aggregates, implementing `BuildingBlocks.Abstractions.Domain.IDomainEvent`.

- Handled in-process, inside this service only.
- Never published to other services directly. To notify other services, map a domain event to an
  integration event (`src/Contracts/IntegrationEvents`) in the Application layer.
