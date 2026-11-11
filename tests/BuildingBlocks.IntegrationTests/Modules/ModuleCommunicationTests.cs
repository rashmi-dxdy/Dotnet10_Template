using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Abstractions.Modules;

namespace BuildingBlocks.IntegrationTests.Modules;

/// <summary>
/// The same two modules, composed as a modular monolith and then split across two hosts by configuration only.
/// </summary>
public sealed class ModuleCommunicationTests
{
    private const string SharedKey = "integration-test-shared-key-0123456789";

    [Fact]
    public async Task InOneProcess_AModuleCallsAnotherOverItsInternalApi_WithTheCorrelationId()
    {
        await using var app = await ModuleTestHost.StartAsync(
            new(StringComparer.Ordinal) { ["HostName"] = "monolith" },
            new InventoryModule(),
            new ShopModule());
        using var client = Client(app);

        var response = await GetEchoAsync(client, "corr-mono");

        Assert.Equal(new EchoResponse("hello world", "corr-mono", "monolith"), response);
    }

    [Fact]
    public async Task InternalApi_RejectsCallers_WithoutTheKey()
    {
        await using var app = await ModuleTestHost.StartAsync(
            new(StringComparer.Ordinal) { ["InternalApi:ApiKey"] = SharedKey },
            new InventoryModule());
        using var client = Client(app);
        var token = TestContext.Current.CancellationToken;

        using var withoutKey = await client.GetAsync(new Uri("/internal/inventory/echo/x", UriKind.Relative), token);
        using var unknownPath = await client.GetAsync(
            new Uri("/internal/inventory/nothing-here", UriKind.Relative),
            token);
        using var wrongKey = new HttpRequestMessage(HttpMethod.Get, "/internal/inventory/echo/x");
        wrongKey.Headers.Add(InternalApiOptions.HeaderName, SharedKey + "x");
        using var withWrongKey = await client.SendAsync(wrongKey, token);
        using var rightKey = new HttpRequestMessage(HttpMethod.Get, "/internal/inventory/echo/x");
        rightKey.Headers.Add(InternalApiOptions.HeaderName, SharedKey);
        using var withKey = await client.SendAsync(rightKey, token);

        Assert.Equal(HttpStatusCode.Forbidden, withoutKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknownPath.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withWrongKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
    }

    [Fact]
    public async Task WithoutAConfiguredKey_OnlyTheProcessItselfCanCallItsInternalApi()
    {
        await using var app = await ModuleTestHost.StartAsync([], new InventoryModule(), new ShopModule());
        using var client = Client(app);
        var token = TestContext.Current.CancellationToken;

        using var direct = await client.GetAsync(new Uri("/internal/inventory/echo/x", UriKind.Relative), token);
        using var throughModule = await client.GetAsync(new Uri("/api/shop/echo/x", UriKind.Relative), token);

        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, throughModule.StatusCode);
    }

    /// <summary>The extraction scenario: no code changes, only Modules:* and InternalApi:ApiKey settings.</summary>
    [Fact]
    public async Task SplitAcrossTwoHosts_TheSameModulesTalkThroughConfigurationAlone()
    {
        await using var inventoryHost = await ModuleTestHost.StartAsync(
            new(StringComparer.Ordinal)
            {
                ["HostName"] = "inventory-host",
                ["Modules:Shop:Enabled"] = "false",
                ["InternalApi:ApiKey"] = SharedKey,
            },
            new InventoryModule(),
            new ShopModule());

        await using var shopHost = await ModuleTestHost.StartAsync(
            new(StringComparer.Ordinal)
            {
                ["HostName"] = "shop-host",
                ["Modules:Inventory:Enabled"] = "false",
                ["Modules:Inventory:BaseAddress"] = ModuleTestHost.BaseAddress(inventoryHost).ToString(),
                ["InternalApi:ApiKey"] = SharedKey,
            },
            new InventoryModule(),
            new ShopModule());

        using var client = Client(shopHost);
        var response = await GetEchoAsync(client, "corr-split");

        Assert.Equal(new EchoResponse("hello world", "corr-split", "inventory-host"), response);

        // The extracted module's endpoints are gone from the other host.
        using var inventoryClient = Client(inventoryHost);
        using var shopOnInventoryHost = await inventoryClient.GetAsync(
            new Uri("/api/shop/echo/x", UriKind.Relative),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, shopOnInventoryHost.StatusCode);
    }

    [Fact]
    public async Task SplitWithoutASharedKey_FailsAtStartup_WithInstructions()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ModuleTestHost.StartAsync(
                new(StringComparer.Ordinal)
                {
                    ["Modules:Inventory:Enabled"] = "false",
                    ["Modules:Inventory:BaseAddress"] = "http://127.0.0.1:9",
                },
                new InventoryModule(),
                new ShopModule()));

        Assert.Contains("InternalApi__ApiKey", exception.Message, StringComparison.Ordinal);
    }

    private static HttpClient Client(Microsoft.AspNetCore.Builder.WebApplication app) =>
        new() { BaseAddress = ModuleTestHost.BaseAddress(app) };

    private static async Task<EchoResponse?> GetEchoAsync(HttpClient client, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/echo/hello%20world");
        request.Headers.Add(CorrelationHeader.Name, correlationId);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<EchoResponse>(TestContext.Current.CancellationToken);
    }
}
