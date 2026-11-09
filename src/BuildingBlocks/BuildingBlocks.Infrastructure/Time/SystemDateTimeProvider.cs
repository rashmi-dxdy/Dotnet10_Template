using BuildingBlocks.Abstractions.Time;

namespace BuildingBlocks.Infrastructure.Time;

/// <summary>
/// <see cref="IDateTimeProvider"/> backed by <see cref="TimeProvider"/>, so tests can substitute
/// a fake <see cref="TimeProvider"/> as well as a fake <see cref="IDateTimeProvider"/>.
/// </summary>
/// <param name="timeProvider">The underlying time provider.</param>
public sealed class SystemDateTimeProvider(TimeProvider timeProvider) : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
