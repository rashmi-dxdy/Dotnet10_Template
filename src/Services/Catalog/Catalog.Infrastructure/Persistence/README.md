# Persistence

EF Core with PostgreSQL for the Catalog service:

- `CatalogDbContext`: this service's own DbContext. Internal to this project; never referenced by another
  service.
- Entity type configurations (`IEntityTypeConfiguration<T>`), applied automatically by the DbContext.
- `Migrations/`: generated EF Core migrations (none yet).
- Repository implementations of the `Domain/Repositories` interfaces.

Registration: `AddCatalogPersistence()` in `DependencyInjection/`. Connection string:
`ConnectionStrings:CatalogDb`, from user secrets or environment variables only.

This service owns its database. No other service reads or writes it. See docs/database.md.
