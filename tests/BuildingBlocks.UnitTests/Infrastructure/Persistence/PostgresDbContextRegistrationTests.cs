using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.UnitTests.Infrastructure.Persistence;

public sealed class PostgresDbContextRegistrationTests
{
    private const string Name = "TestDb";
    private const string Prefix = "TST_";

    // Port 1 on loopback: nothing listens there, so the connection is refused immediately.
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=test;Username=app;Password=s3cr3t-value;Timeout=2";

    [Fact]
    public void AddPostgresDbContext_RegistersContextWithNpgsqlAndSharedSettings()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:CommandTimeoutSeconds"] = "42",
        });
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(42, context.Database.GetCommandTimeout());

        // Own migrations history table, so services sharing one database never mix migration records.
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        Assert.Equal("TST_EFMigrationsHistory", relational.MigrationsHistoryTableName);
    }

    [Fact]
    public void AddPostgresDbContext_RegistersReadyHealthCheckAndStartupCheck()
    {
        using var provider = BuildProvider();

        var registration = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);
        Assert.Equal("postgresql-TestDb", registration.Name);
        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Single(
            provider.GetServices<IHostedService>(),
            s => string.Equals(s.GetType().Name, "PostgresStartupCheck", StringComparison.Ordinal));
    }

    [Fact]
    public void AddPostgresDbContext_WithoutConnectionString_FailsAtRegistration()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPostgresDbContext<TestDbContext>(new ConfigurationBuilder().Build(), Name, Prefix));

        Assert.Contains("ConnectionStrings:TestDb", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthCheck_WhenDatabaseIsUnreachable_IsUnhealthyWithAClearReason()
    {
        await using var provider = BuildProvider();
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(report.Entries).Value;
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("app@127.0.0.1:1/test is unavailable", entry.Description, StringComparison.Ordinal);
        Assert.Contains("cannot reach the server", entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-value", entry.Description, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"ConnectionStrings:{Name}"] = UnreachableDatabase,
            })
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresDbContext<TestDbContext>(configuration, Name, Prefix);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
