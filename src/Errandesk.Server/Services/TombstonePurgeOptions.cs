namespace Errandesk.Server.Services;

/// <summary>
/// Timing of the tombstone purge.
/// </summary>
public sealed class TombstonePurgeOptions
{
    /// <summary>Tombstones deleted longer ago than this are removed.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Time after startup before the first purge.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Time between two purges. <see cref="Timeout.InfiniteTimeSpan"/> stops the periodic purges.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);
}
