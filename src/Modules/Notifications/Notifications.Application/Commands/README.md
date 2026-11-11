# Commands

Use cases that change state. One folder per use case:

- `<UseCase>Command`: implements `ICommand<TResult>`, an immutable record.
- `<UseCase>CommandHandler`: implements `ICommandHandler<TCommand, TResult>` (`BuildingBlocks.Application.Cqrs`). MediatR finds it by assembly scan; dispatch with `ISender.Send`.

Return a minimal result, such as an identifier. Commands go through the domain model (aggregates).
