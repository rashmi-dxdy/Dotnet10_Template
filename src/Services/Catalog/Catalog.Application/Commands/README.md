# Commands

Use cases that change state. One folder per use case:

- `<UseCase>Command`: implements `ICommand<TResult>`, an immutable record.
- `<UseCase>CommandHandler`: implements `ICommandHandler<TCommand, TResult>`.

Return a minimal result, such as an identifier. Commands go through the domain model (aggregates).
