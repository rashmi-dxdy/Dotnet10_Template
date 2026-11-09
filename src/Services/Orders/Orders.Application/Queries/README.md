# Queries

Use cases that read state. One folder per use case:

- `<UseCase>Query`: implements `IQuery<TResult>`.
- `<UseCase>QueryHandler`: implements `IQueryHandler<TQuery, TResult>` (`BuildingBlocks.Application.Cqrs`). MediatR finds it by assembly scan; dispatch with `ISender.Send`.
- The result DTO.

Queries never change state and may read directly from a read model instead of loading aggregates.
