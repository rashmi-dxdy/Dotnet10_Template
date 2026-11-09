# Value objects

Immutable types defined entirely by their values (for example, a quantity or an email address).

- Prefer `sealed record` or `readonly record struct`.
- Validate invariants when the value is created, so an invalid instance can never exist.
