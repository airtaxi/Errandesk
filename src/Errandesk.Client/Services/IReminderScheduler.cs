using Errandesk.Client.Models;
using Errandesk.Contracts;

namespace Errandesk.Client.Services;

/// <summary>
/// Shows the reminders of the signed-in user on this device, with the notification mechanism of the platform.
/// </summary>
public interface IReminderScheduler
{
    /// <summary>
    /// The platform this device registers as, one of <see cref="DevicePlatforms.All"/>.
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Raised on the UI thread when a reminder is due and the platform shows it inside the app.
    /// </summary>
    event EventHandler<ReminderNotification>? InAppReminderRequested;

    /// <summary>
    /// Raised, possibly on another thread, when the push token of this device changes after <see cref="PrepareAsync"/>.
    /// </summary>
    event EventHandler<string>? PushTokenChanged;

    /// <summary>
    /// Prepares notifications: creates channels and asks for permission where the platform needs it.
    /// </summary>
    /// <param name="serverInfo">The server capabilities, or <see langword="null"/> when they could not be read.</param>
    /// <returns>The push token when the server delivers this device's reminders by push; <see langword="null"/> when this device schedules them itself.</returns>
    Task<string?> PrepareAsync(ServerInfoResponse? serverInfo, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the scheduled reminders with <paramref name="reminders"/>. Reminders that are not in the future are left out.
    /// </summary>
    Task ScheduleAsync(IReadOnlyList<ReminderNotification> reminders, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every reminder this app scheduled or showed.
    /// </summary>
    Task ClearAsync();
}
