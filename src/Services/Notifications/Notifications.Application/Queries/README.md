# Queries

Use cases that read state. One folder per use case:

- `<UseCase>Query`: implements `IQuery<TResult>`.
- `<UseCase>QueryHandler`: implements `IQueryHandler<TQuery, TResult>`.
- The result DTO.

Queries never change state and may read directly from a read model instead of loading aggregates.
