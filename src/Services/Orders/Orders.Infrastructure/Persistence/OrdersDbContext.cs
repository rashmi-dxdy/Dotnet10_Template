using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the Orders service. Owned by this service alone: no other service may reference
/// it or read its tables. All services currently share one database, so every table of this service is
/// prefixed with <see cref="TablePrefix"/> (see docs/database.md).
/// </summary>
/// <remarks>
/// Add entity type configurations (<c>IEntityTypeConfiguration&lt;T&gt;</c>) in this folder; they are applied
/// automatically. Generate migrations into <c>Persistence/Migrations</c> (see docs/database.md).
/// </remarks>
/// <param name="options">The options configured by <c>AddOrdersPersistence</c>.</param>
internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    /// <summary>The name of this service connection string: <c>ConnectionStrings:OrdersDb</c>.</summary>
    public const string ConnectionStringName = "OrdersDb";

    /// <summary>Prefix of every table this service owns, e.g. <c>ORD_Orders</c>. Never change it once tables exist.</summary>
    public const string TablePrefix = "ORD_";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);

        // Last, so tables named explicitly in the configurations above are prefixed too.
        modelBuilder.ApplyTablePrefix(TablePrefix);
    }
}
