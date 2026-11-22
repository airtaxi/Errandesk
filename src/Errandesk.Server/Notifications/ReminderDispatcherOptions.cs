namespace Errandesk.Server.Notifications;

/// <summary>
/// Timing of the reminder dispatcher.
/// </summary>
public sealed class ReminderDispatcherOptions
{
    /// <summary>Time between two checks for due reminders. <see cref="Timeout.InfiniteTimeSpan"/> stops the periodic checks.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>A claim older than this is treated as abandoned, for example after a crash, and the reminder can be claimed again.</summary>
    public TimeSpan ClaimTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Reminders that are due longer than this are marked as handled without a notification.</summary>
    public TimeSpan MaximumLateness { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Upper bound of reminders handled in one check, so one check cannot run unbounded.</summary>
    public int MaximumRemindersPerCheck { get; init; } = 500;
}
