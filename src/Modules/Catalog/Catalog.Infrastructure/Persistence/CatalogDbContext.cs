using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the Catalog service. Owned by this module alone: no other module may reference
/// it or read its tables. All modules currently share one database, so every table of this module is
/// prefixed with <see cref="TablePrefix"/> (see docs/database.md).
/// </summary>
/// <remarks>
/// Add entity type configurations (<c>IEntityTypeConfiguration&lt;T&gt;</c>) in this folder; they are applied
/// automatically. Generate migrations into <c>Persistence/Migrations</c> (see docs/database.md).
/// </remarks>
/// <param name="options">The options configured by <c>AddCatalogPersistence</c>.</param>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    /// <summary>The name of this module connection string: <c>ConnectionStrings:CatalogDb</c>.</summary>
    public const string ConnectionStringName = "CatalogDb";

    /// <summary>Prefix of every table this module owns, e.g. <c>CAT_Products</c>. Never change it once tables exist.</summary>
    public const string TablePrefix = "CAT_";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        base.OnModelCreating(modelBuilder);

        // Last, so tables named explicitly in the configurations above are prefixed too.
        modelBuilder.ApplyTablePrefix(TablePrefix);
    }
}
