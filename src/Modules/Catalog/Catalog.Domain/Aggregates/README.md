# Aggregates

Aggregate roots: the consistency boundary of the domain and the only entry point for changing the
objects inside it.

- One aggregate is modified per transaction.
- Reference other aggregates by identifier only, never by object reference.
- Aggregates raise domain events (`Events/`) to describe what happened.
