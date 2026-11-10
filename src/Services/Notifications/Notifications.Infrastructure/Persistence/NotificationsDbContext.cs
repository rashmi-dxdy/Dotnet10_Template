using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the Notifications service. Owned by this service alone: no other service may reference
/// it or read its tables. All services currently share one database, so every table of this service is
/// prefixed with <see cref="TablePrefix"/> (see docs/database.md).
/// </summary>
/// <remarks>
/// Add entity type configurations (<c>IEntityTypeConfiguration&lt;T&gt;</c>) in this folder; they are applied
/// automatically. Generate migrations into <c>Persistence/Migrations</c> (see docs/database.md).
/// </remarks>
/// <param name="options">The options configured by <c>AddNotificationsPersistence</c>.</param>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <summary>The name of this service connection string: <c>ConnectionStrings:NotificationsDb</c>.</summary>
    public const string ConnectionStringName = "NotificationsDb";

    /// <summary>Prefix of every table this service owns, e.g. <c>NTF_Messages</c>. Never change it once tables exist.</summary>
    public const string TablePrefix = "NTF_";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);

        // Last, so tables named explicitly in the configurations above are prefixed too.
        modelBuilder.ApplyTablePrefix(TablePrefix);
    }
}
