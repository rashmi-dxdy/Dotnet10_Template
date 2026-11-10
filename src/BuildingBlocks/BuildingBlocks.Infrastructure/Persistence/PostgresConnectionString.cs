using System.Reflection;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// A validated PostgreSQL connection string read from <c>ConnectionStrings:&lt;Name&gt;</c>.
/// </summary>
public sealed class PostgresConnectionString
{
    private PostgresConnectionString(string name, string value, string target)
    {
        Name = name;
        Value = value;
        Target = target;
    }

    /// <summary>Gets the connection string name, e.g. <c>CatalogDb</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the connection string. Contains credentials: never log it.</summary>
    public string Value { get; }

    /// <summary>
    /// Gets a description that is safe to log: <c>user@host:port/database</c>, never the password.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Reads and validates the connection string. Fails with an explanation of how to set it, without ever
    /// echoing the configured value (it may contain a password).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="name">The connection string name, e.g. <c>CatalogDb</c>.</param>
    /// <returns>The validated connection string.</returns>
    /// <exception cref="InvalidOperationException">The connection string is missing or invalid.</exception>
    public static PostgresConnectionString FromConfiguration(IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var value = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"PostgreSQL connection string 'ConnectionStrings:{name}' is not configured. Credentials are never "
                + "stored in appsettings*.json. Development: run `dotnet user-secrets set \"ConnectionStrings:"
                + $"{name}\" \"Host=localhost;Port=5432;Database=...;Username=...;Password=...\"` in the host "
                + $"project folder. Docker/CI: set the environment variable 'ConnectionStrings__{name}'. "
                + "See docs/database.md.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            // MA0054 wants the caught exception as InnerException, but Npgsql's message can quote part of the
            // connection string (possibly the password) and the inner exception would end up in the fatal log.
#pragma warning disable MA0054
            throw new InvalidOperationException(
                $"PostgreSQL connection string 'ConnectionStrings:{name}' is not a valid Npgsql connection string. "
                + "Expected the form 'Host=...;Port=5432;Database=...;Username=...;Password=...'. "
                + "See docs/database.md.");
#pragma warning restore MA0054
        }

        var missing = new[] { (Key: "Host", builder.Host), (Key: "Database", builder.Database) }
            .Where(part => string.IsNullOrWhiteSpace(part.Item2))
            .Select(part => part.Key)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"PostgreSQL connection string 'ConnectionStrings:{name}' has no {string.Join(" or ", missing)}. "
                + "Every service connects to its own database. See docs/database.md.");
        }

        // Shows which service holds a connection (pg_stat_activity), useful with a shared dev server.
        if (string.IsNullOrEmpty(builder.ApplicationName)
            && Assembly.GetEntryAssembly()?.GetName().Name is { } applicationName)
        {
            builder.ApplicationName = applicationName;
        }

        var target = $"{builder.Username ?? "(default user)"}@{builder.Host}:{builder.Port}/{builder.Database}";
        return new PostgresConnectionString(name, builder.ConnectionString, target);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Target})";
}
