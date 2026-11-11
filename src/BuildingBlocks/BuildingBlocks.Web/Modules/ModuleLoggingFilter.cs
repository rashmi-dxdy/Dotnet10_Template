using BuildingBlocks.Hosting.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// Adds the <see cref="LogProperties.Module"/> property to every log entry written while one of the module's
/// endpoints runs (handlers, MediatR behaviors, EF Core) and to the request completion entry.
/// </summary>
/// <param name="module">The module name.</param>
internal sealed class ModuleLoggingFilter(string module) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        context.HttpContext.RequestServices.GetService<IDiagnosticContext>()?.Set(LogProperties.Module, module);

        using (LogContext.PushProperty(LogProperties.Module, module))
        {
            return await next(context).ConfigureAwait(false);
        }
    }
}
