using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Per-service table prefixes. All services currently share ONE database, so every table name starts with
/// its owning service's prefix (e.g. <c>CAT_Products</c>, <c>ORD_Orders</c>). The prefix marks ownership and
/// lets each service's tables move to a separate database later without renaming anything.
/// </summary>
public static partial class TablePrefix
{
    /// <summary>
    /// Prefixes every table in the model that does not already start with <paramref name="prefix"/>:
    /// entity tables, join tables of many-to-many relationships, owned types and inheritance hierarchies.
    /// Views and keyless query types are left alone. Call it LAST in <c>OnModelCreating</c>, after all entity
    /// configurations, so explicit <c>ToTable("...")</c> names are prefixed too.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="prefix">The service prefix, e.g. <c>CAT_</c>.</param>
    /// <returns>The same model builder, for chaining.</returns>
    public static ModelBuilder ApplyTablePrefix(this ModelBuilder modelBuilder, string prefix)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        EnsureValid(prefix);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is not null && !tableName.StartsWith(prefix, StringComparison.Ordinal))
            {
                entityType.SetTableName(prefix + tableName);
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// The service's own EF Core migrations history table, e.g. <c>CAT_EFMigrationsHistory</c>, so services
    /// sharing a database never mix their migration records.
    /// </summary>
    /// <param name="prefix">The service prefix, e.g. <c>CAT_</c>.</param>
    /// <returns>The history table name.</returns>
    public static string MigrationsHistoryTable(string prefix)
    {
        EnsureValid(prefix);
        return prefix + "EFMigrationsHistory";
    }

    /// <summary>
    /// Requires 2-10 upper-case letters or digits starting with a letter, followed by <c>_</c>
    /// (e.g. <c>CAT_</c>, <c>ORD_</c>).
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    /// <exception cref="ArgumentException">The prefix does not follow the convention.</exception>
    public static void EnsureValid(string prefix)
    {
        if (prefix is null || !PrefixFormat().IsMatch(prefix))
        {
            throw new ArgumentException(
                $"Table prefix '{prefix}' is invalid. Use 2-10 upper-case letters or digits starting with a letter, "
                + "followed by '_', e.g. 'CAT_'.",
                nameof(prefix));
        }
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}_$", RegexOptions.None, 1000)]
    private static partial Regex PrefixFormat();
}
