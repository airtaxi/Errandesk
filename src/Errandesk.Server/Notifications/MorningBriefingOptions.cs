namespace Errandesk.Server.Notifications;

/// <summary>
/// Timing of the morning briefing scheduler.
/// </summary>
public sealed class MorningBriefingOptions
{
    /// <summary>Time between two checks for due briefings. <see cref="Timeout.InfiniteTimeSpan"/> stops the periodic checks.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>A claim older than this is treated as abandoned, so a briefing that failed or whose server stopped is tried again.</summary>
    public TimeSpan ClaimTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long after the chosen time a briefing is still written, for example after the server was down or the briefing was turned on later in the day.
    /// Later in the day no briefing is written until the next morning.
    /// </summary>
    public TimeSpan MaximumLateness { get; init; } = TimeSpan.FromHours(4);
}
