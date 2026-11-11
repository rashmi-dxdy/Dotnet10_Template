using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Abstractions.Modules;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace BuildingBlocks.UnitTests.Infrastructure.Modules;

public sealed class ModuleClientTests
{
    private const string SharedKey = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("items/7?x=1", "http://10.0.0.5:8080", "http://10.0.0.5:8080/internal/alpha/items/7?x=1")]
    [InlineData("items", "https://gateway/alpha-svc/", "https://gateway/alpha-svc/internal/alpha/items")]
    public void Rebase_MovesTheRequestOntoTheModulesAddress(string path, string target, string expected)
    {
        var request = new Uri(ModuleClientRegistration.PlaceholderBaseAddress("alpha"), path);

        Assert.Equal(expected, ModuleClientHandler.Rebase(request, new Uri(target)).ToString());
    }

    [Fact]
    public async Task Request_GoesToTheConfiguredAddress_WithTheKeyAndCorrelationId()
    {
        var server = new RecordingHandler();
        await using var provider = Provider(server, Remote("http://alpha-host:9000"));

        using var response = await provider.GetRequiredService<AlphaClient>().Http
            .GetAsync(new Uri("items/7", UriKind.Relative), TestContext.Current.CancellationToken);

        var request = Assert.Single(server.Requests);
        Assert.Equal("http://alpha-host:9000/internal/alpha/items/7", request.Uri.ToString());
        Assert.Equal(SharedKey, request.Header(InternalApiOptions.HeaderName));
        Assert.Equal("corr-1", request.Header(CorrelationHeader.Name));
    }

    [Fact]
    public async Task UnsafeMethods_AreNeverRetried_SafeMethodsAre()
    {
        var server = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        await using var provider = Provider(server, Remote("http://alpha-host:9000"));
        var http = provider.GetRequiredService<AlphaClient>().Http;
        var token = TestContext.Current.CancellationToken;

        using (await http.PostAsJsonAsync(new Uri("commands", UriKind.Relative), new { Id = 1 }, token))
        {
            Assert.Single(server.Requests);
        }

        server.Requests.Clear();
        using (await http.GetAsync(new Uri("items", UriKind.Relative), token))
        {
            Assert.True(server.Requests.Count > 1, "A failed GET should have been retried.");
        }
    }

    [Fact]
    public async Task UnreachableModule_ThrowsModuleCallException_NamingTheModule()
    {
        await using var provider = Provider(primary: null, Remote("http://127.0.0.1:1"));
        var http = provider.GetRequiredService<AlphaClient>().Http;

        var exception = await Assert.ThrowsAsync<ModuleCallException>(
            () => http.PostAsync(
                new Uri("commands", UriKind.Relative),
                content: null,
                TestContext.Current.CancellationToken));

        Assert.Equal("alpha", exception.Module);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task EnsureModuleSuccess_TurnsAProblemResponseIntoModuleCallException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { title = "Duplicate", detail = "Item 7 exists." }),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://alpha-host/internal/alpha/items"),
        };

        var exception = await Assert.ThrowsAsync<ModuleCallException>(
            () => response.EnsureModuleSuccessAsync("alpha", TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("Duplicate Item 7 exists.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("POST /internal/alpha/items", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupCheck_FailsWhenTheModuleIsNeitherHostedHereNorConfigured()
    {
        await using var provider = Provider(new RecordingHandler(), []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(provider));

        Assert.Contains("module 'alpha' is not hosted in this process", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Modules__alpha__BaseAddress", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupCheck_FailsWhenARemoteModuleIsCalledWithoutASharedKey()
    {
        await using var provider = Provider(
            new RecordingHandler(),
            new(StringComparer.Ordinal) { ["Modules:Alpha:BaseAddress"] = "http://alpha-host:9000" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(provider));

        Assert.Contains("InternalApi__ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModuleClient_RejectsAnInvalidBaseAddress()
    {
        var configuration = Configuration(new(StringComparer.Ordinal) { ["Modules:Alpha:BaseAddress"] = "alpha-host" });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddModuleClient<AlphaClient, AlphaClient>(configuration, "alpha"));

        Assert.Contains("Modules__alpha__BaseAddress", exception.Message, StringComparison.Ordinal);
    }

    private static async Task StartAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private static Dictionary<string, string?> Remote(string address) => new(StringComparer.Ordinal)
    {
        ["Modules:Alpha:BaseAddress"] = address,
        ["InternalApi:ApiKey"] = SharedKey,
    };

    private static ServiceProvider Provider(HttpMessageHandler? primary, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICorrelationIdAccessor>(new FixedCorrelationId("corr-1"));
        var client = services.AddModuleClient<AlphaClient, AlphaClient>(Configuration(settings), "alpha");
        if (primary is not null)
        {
            client.ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        // Keep the standard retry policy, but without its multi-second backoff.
        services.Configure<HttpStandardResilienceOptions>($"{client.Name}-standard", options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });

        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal sealed class AlphaClient(HttpClient http)
    {
        public HttpClient Http { get; } = http;
    }

    private sealed class FixedCorrelationId(string id) : ICorrelationIdAccessor
    {
        public string? CorrelationId => id;
    }

    private sealed record RecordedRequest(Uri Uri, Dictionary<string, string> Headers)
    {
        public string? Header(string name) => Headers.GetValueOrDefault(name);
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.Ordinal)));
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
