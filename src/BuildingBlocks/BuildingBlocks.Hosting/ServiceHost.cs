using BuildingBlocks.Hosting.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Templates;

namespace BuildingBlocks.Hosting;

/// <summary>
/// Runs a service host with the logging guarantees every service needs: a bootstrap logger before
/// configuration is read, a fatal log entry if startup or the host crashes, and a final flush so the
/// last log entries are never lost on shutdown.
/// </summary>
public static class ServiceHost
{
    /// <summary>
    /// Runs <paramref name="run"/> (which builds and runs the host) and returns the process exit code.
    /// </summary>
    /// <param name="run">Builds the host and awaits <c>RunAsync()</c>.</param>
    /// <returns><c>0</c> after a normal shutdown, <c>1</c> if the host terminated with an exception.</returns>
    public static async Task<int> RunAsync(Func<Task> run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Replaced by the configured logger once the host is built (see AddServiceLogging).
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Console(new ExpressionTemplate(LogFormats.Json))
            .CreateBootstrapLogger();

        try
        {
            await run().ConfigureAwait(false);
            return 0;
        }
        catch (HostAbortedException)
        {
            // Thrown on purpose by design-time tools (e.g. `dotnet ef`) after they get the host. Not a crash.
            throw;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }
}
