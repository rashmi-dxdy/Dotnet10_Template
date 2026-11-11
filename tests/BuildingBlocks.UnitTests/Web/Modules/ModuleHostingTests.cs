using BuildingBlocks.Abstractions.Modules;
using BuildingBlocks.Web.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.UnitTests.Web.Modules;

public sealed class ModuleHostingTests
{
    [Fact]
    public void AddModules_RegistersOnlyTheModulesEnabledInConfiguration()
    {
        var alpha = new FakeModule("alpha");
        var beta = new FakeModule("beta");
        var builder = Builder(new(StringComparer.Ordinal) { ["Modules:Beta:Enabled"] = "false" });

        builder.AddModules(alpha, beta);
        using var provider = builder.Services.BuildServiceProvider();
        var host = provider.GetRequiredService<IModuleHost>();

        Assert.True(alpha.ServicesAdded);
        Assert.False(beta.ServicesAdded);
        Assert.True(host.IsHosted("alpha"));
        Assert.False(host.IsHosted("beta"));
        Assert.Equal(["alpha"], host.HostedModules);
    }

    [Theory]
    [InlineData("Catalog")]
    [InlineData("1st")]
    [InlineData("with space")]
    [InlineData("")]
    public void AddModules_RejectsInvalidNames(string name)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Builder().AddModules(new FakeModule(name)));

        Assert.Contains("Invalid module name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModules_RejectsDuplicateNames()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Builder().AddModules(new FakeModule("alpha"), new FakeModule("alpha")));

        Assert.Contains("unique: alpha", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModules_WithEveryModuleDisabled_FailsWithInstructions()
    {
        var builder = Builder(new(StringComparer.Ordinal) { ["Modules:Alpha:Enabled"] = "false" });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddModules(new FakeModule("alpha")));

        Assert.Contains("Modules__<Name>__Enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModules_WithAShortInternalApiKey_FailsWithoutRevealingIt()
    {
        var builder = Builder(new(StringComparer.Ordinal) { ["InternalApi:ApiKey"] = "too-short-key" });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddModules(new FakeModule("alpha")));

        Assert.Contains("InternalApi__ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("too-short-key", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://[::]:8080", "http://[::1]:8080/")]
    [InlineData("http://0.0.0.0:5000", "http://127.0.0.1:5000/")]
    [InlineData("http://+:80", "http://localhost/")]
    [InlineData("http://*:5100", "http://localhost:5100/")]
    [InlineData("https://localhost:5001", "https://localhost:5001/")]
    [InlineData("http://127.0.0.1:0", null)]
    [InlineData("not a url", null)]
    public void ToLoopback_MakesListeningAddressesConnectable(string address, string? expected) =>
        Assert.Equal(expected, WebModuleHost.ToLoopback(address)?.ToString(), StringComparer.Ordinal);

    [Fact]
    public void InternalApiKey_WithoutConfiguration_IsRandomPerProcess_AndNeverPrinted()
    {
        var first = new InternalApiKey(configuredKey: null);
        var second = new InternalApiKey(configuredKey: "");

        Assert.False(first.IsShared);
        Assert.Matches("^[0-9A-F]{64}$", first.Value);
        Assert.NotEqual(first.Value, second.Value, StringComparer.Ordinal);
        Assert.True(first.Matches(first.Value));
        Assert.False(first.Matches(second.Value));
        Assert.False(first.Matches(null));
        Assert.DoesNotContain(first.Value, first.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InternalApiKey_WhenConfigured_IsShared()
    {
        var key = new InternalApiKey(new string('k', InternalApiOptions.MinimumApiKeyLength));

        Assert.True(key.IsShared);
        Assert.True(key.Matches(new string('k', InternalApiOptions.MinimumApiKeyLength)));
        Assert.DoesNotContain("kkkk", key.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationBuilder Builder(Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        return builder;
    }

    private sealed class FakeModule(string name) : IModule
    {
        public string Name => name;

        public bool ServicesAdded { get; private set; }

        public void AddServices(IServiceCollection services, IConfiguration configuration) => ServicesAdded = true;

        public void MapEndpoints(ModuleRouteGroups routes)
        {
            // No endpoints: registration only.
        }
    }
}
