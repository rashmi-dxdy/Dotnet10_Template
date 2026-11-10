using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// Central EF Core + PostgreSQL setup. Each service calls it once, for its OWN DbContext, connection string
/// and table prefix. There is deliberately no shared DbContext.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> with the Npgsql provider and the shared settings from the
    /// <c>Database</c> section, plus a PostgreSQL health check (tag <c>ready</c>) and a startup connectivity
    /// check that logs a clear message if the database is unreachable. The service's migrations history is
    /// kept in its own <c>&lt;prefix&gt;EFMigrationsHistory</c> table, so services can share one database.
    /// </summary>
    /// <typeparam name="TContext">The service's own DbContext. It must call <c>ApplyTablePrefix</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="connectionStringName">The name under <c>ConnectionStrings</c>, e.g. <c>CatalogDb</c>.</param>
    /// <param name="tablePrefix">The service's table prefix, e.g. <c>CAT_</c>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is missing or invalid.</exception>
    /// <exception cref="ArgumentException">The table prefix does not follow the convention.</exception>
    public static IServiceCollection AddPostgresDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName,
        string tablePrefix)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var migrationsHistoryTable = TablePrefix.MigrationsHistoryTable(tablePrefix);

        // Fails at startup, with instructions, rather than on the first query.
        var connectionString = PostgresConnectionString.FromConfiguration(configuration, connectionStringName);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                $"'{DatabaseOptions.SectionName}' settings are invalid: timeouts and delays must be positive and "
                + "MaxRetryCount must not be negative.")
            .ValidateOnStart();

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options.UseNpgsql(connectionString.Value, npgsql =>
            {
                npgsql.MigrationsHistoryTable(migrationsHistoryTable);
                npgsql.CommandTimeout(database.CommandTimeoutSeconds);
                if (database.MaxRetryCount > 0)
                {
                    npgsql.EnableRetryOnFailure(
                        database.MaxRetryCount,
                        TimeSpan.FromSeconds(database.MaxRetryDelaySeconds),
                        errorCodesToAdd: null);
                }
            });

            options.EnableDetailedErrors(database.EnableDetailedErrors);
            options.EnableSensitiveDataLogging(database.EnableSensitiveDataLogging);
        });

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            $"postgresql-{connectionStringName}",
            serviceProvider => new PostgresHealthCheck(connectionString, ProbeTimeout(serviceProvider)),
            HealthStatus.Unhealthy,
            [HealthCheckTags.Ready]));

        // AddSingleton, not AddHostedService: one startup check per registered database.
        services.AddSingleton<IHostedService>(serviceProvider => new PostgresStartupCheck(
            connectionString,
            ProbeTimeout(serviceProvider),
            serviceProvider.GetRequiredService<ILogger<PostgresStartupCheck>>()));

        return services;
    }

    private static TimeSpan ProbeTimeout(IServiceProvider serviceProvider) =>
        TimeSpan.FromSeconds(
            serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectivityProbeTimeoutSeconds);
}
