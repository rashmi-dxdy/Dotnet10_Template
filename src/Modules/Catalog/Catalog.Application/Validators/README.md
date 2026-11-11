# Validators

Input validation for commands and queries: shape, format and required values.
Business invariants belong in the Domain layer, not here.

Write one FluentValidation `AbstractValidator<TCommand>` (or `<TQuery>`) per request. Validators in this
assembly are registered automatically by `AddCqrs`, and `ValidationBehavior` runs them before the
handler. Any failure throws `FluentValidation.ValidationException`, which the API hosts return as a
400 validation problem.
