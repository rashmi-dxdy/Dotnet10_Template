using BuildingBlocks.Web.Correlation;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.UnitTests.Web.Correlation;

public sealed class CorrelationIdMiddlewareTests
{
    private const string Header = CorrelationIdMiddleware.HeaderName;

    [Fact]
    public async Task InvokeAsync_ReusesValidIncomingId()
    {
        var context = CreateContext("order-42.retry_1:a");

        var seen = await InvokeAsync(context);

        Assert.Equal("order-42.retry_1:a", seen);
        Assert.Equal("order-42.retry_1:a", context.Response.Headers[Header].ToString());
    }

    [Fact]
    public async Task InvokeAsync_GeneratesId_WhenHeaderIsMissing()
    {
        var context = CreateContext(header: null);

        var seen = await InvokeAsync(context);

        Assert.Matches("^[0-9a-f]{32}$", seen);
        Assert.Equal(seen, context.Response.Headers[Header].ToString());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("line\nbreak")]
    public async Task InvokeAsync_ReplacesUnsafeId(string unsafeId)
    {
        var context = CreateContext(unsafeId);

        var seen = await InvokeAsync(context);

        Assert.NotEqual(unsafeId, seen, StringComparer.Ordinal);
        Assert.Matches("^[0-9a-f]{32}$", seen);
    }

    [Fact]
    public async Task InvokeAsync_ReplacesTooLongId()
    {
        var context = CreateContext(new string('a', 65));

        var seen = await InvokeAsync(context);

        Assert.Matches("^[0-9a-f]{32}$", seen);
    }

    private static DefaultHttpContext CreateContext(string? header)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers[Header] = header;
        }

        return context;
    }

    /// <summary>Runs the middleware and returns the correlation ID visible to the next middleware.</summary>
    private static async Task<string?> InvokeAsync(HttpContext context)
    {
        string? seen = null;
        var middleware = new CorrelationIdMiddleware(next =>
        {
            seen = next.GetCorrelationId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        return seen;
    }
}
