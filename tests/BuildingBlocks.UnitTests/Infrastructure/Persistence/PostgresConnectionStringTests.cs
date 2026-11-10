using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.UnitTests.Infrastructure.Persistence;

public sealed class PostgresConnectionStringTests
{
    private const string Name = "TestDb";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_WhenMissing_ExplainsHowToConfigureIt(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PostgresConnectionString.FromConfiguration(Configuration(value), Name));

        Assert.Contains("ConnectionStrings:TestDb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__TestDb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_WhenInvalid_DoesNotEchoTheValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PostgresConnectionString.FromConfiguration(
            Configuration("Host=db;Database=orders;Pasword=s3cr3t-value"),
            Name));

        Assert.Contains("not a valid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-value", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("Database=orders;Username=app", "Host")]
    [InlineData("Host=db;Username=app", "Database")]
    public void FromConfiguration_WhenHostOrDatabaseIsMissing_SaysWhich(string value, string missingPart)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PostgresConnectionString.FromConfiguration(Configuration(value), Name));

        Assert.Contains($"has no {missingPart}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_DescribesTheDatabase_WithoutThePassword()
    {
        var connectionString = PostgresConnectionString.FromConfiguration(
            Configuration("Host=db;Port=5433;Database=orders;Username=app;Password=s3cr3t-value"),
            Name);

        Assert.Equal("app@db:5433/orders", connectionString.Target);
        Assert.DoesNotContain("s3cr3t-value", connectionString.ToString(), StringComparison.Ordinal);
        Assert.Contains("s3cr3t-value", connectionString.Value, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"ConnectionStrings:{Name}"] = connectionString,
            })
            .Build();
}
