namespace BuildingBlocks.Abstractions.Time;

/// <summary>
/// Provides the current time. Inject this instead of calling <see cref="DateTimeOffset.UtcNow"/>
/// so time-dependent logic is deterministic in tests.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Gets the current UTC date and time.</summary>
    DateTimeOffset UtcNow { get; }
}
