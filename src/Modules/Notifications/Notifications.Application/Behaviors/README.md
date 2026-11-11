# Behaviors

Cross-cutting concerns around handlers, such as logging, validation and transactions.

Implement them as MediatR pipeline behaviors (`IPipelineBehavior<TRequest, TResponse>`) and register
them in `DependencyInjection/` with `cfg.AddOpenBehavior(typeof(MyBehavior<,>))`. Behaviors shared by
every module, such as `LoggingBehavior`, live in `BuildingBlocks.Application/Behaviors`.
