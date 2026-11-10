namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// EF Core settings shared by every service, bound from the <c>Database</c> configuration section.
/// Connection strings are NOT here: each service reads its own from <c>ConnectionStrings:&lt;Name&gt;</c>.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Gets or sets the command timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets how often a failed operation is retried on transient errors. <c>0</c> disables retries.</summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>Gets or sets the maximum delay between retries in seconds.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>Gets or sets how long startup and health probes wait for a connection, in seconds.</summary>
    public int ConnectivityProbeTimeoutSeconds { get; set; } = 5;

    /// <summary>Gets or sets a value indicating whether EF Core reports detailed errors. Development only.</summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether EF Core logs parameter values and entity data.
    /// Leave <see langword="false"/>: they can contain personal or secret data.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    internal bool IsValid() =>
        CommandTimeoutSeconds > 0
        && MaxRetryCount >= 0
        && MaxRetryDelaySeconds > 0
        && ConnectivityProbeTimeoutSeconds > 0;
}
