# Behaviors

Cross-cutting concerns around handlers, such as logging, validation and transactions.

There is no mediator library. Implement behaviors as decorators over `ICommandHandler<,>` /
`IQueryHandler<,>` and register them in `DependencyInjection/`.
