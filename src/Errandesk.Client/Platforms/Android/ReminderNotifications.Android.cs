using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Errandesk.Client.Models;

namespace Errandesk.Client.Droid;

/// <summary>
/// Posts reminder notifications on the <c>reminders</c> channel and reads the task a tapped notification opens.
/// </summary>
public static class ReminderNotifications
{
    /// <summary>The notification channel of reminders, which pushed reminders also name.</summary>
    public const string ChannelId = "reminders";

    /// <summary>The intent extra that carries the task ID, as in pushed reminders.</summary>
    public const string TaskIdExtra = "taskId";

    /// <summary>The intent extra that carries the list ID, as in pushed reminders.</summary>
    public const string ListIdExtra = "listId";

    private const string DefaultChannelName = "Reminders";

    /// <summary>
    /// Creates the reminders channel, or renames it to <paramref name="channelName"/> when it exists.
    /// </summary>
    public static void EnsureChannel(Context context, string? channelName = null)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || context.GetSystemService(Context.NotificationService) is not NotificationManager notificationManager) return;
        if (channelName is null && notificationManager.GetNotificationChannel(ChannelId) is not null) return;

        notificationManager.CreateNotificationChannel(new NotificationChannel(ChannelId, channelName ?? DefaultChannelName, NotificationImportance.High));
    }

    /// <summary>
    /// Shows <paramref name="reminder"/>. Tapping it opens the task in the app.
    /// </summary>
    public static void Post(Context context, ReminderNotification reminder)
    {
        EnsureChannel(context);
        if (NotificationManagerCompat.From(context) is not { } notificationManager || !notificationManager.AreNotificationsEnabled()) return;

        var notificationId = GetNotificationId(reminder.TaskId);
        var openIntent = new Intent(context, typeof(MainActivity));
        openIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        openIntent.PutExtra(TaskIdExtra, reminder.TaskId.ToString());
        openIntent.PutExtra(ListIdExtra, reminder.ListId.ToString());
        var contentIntent = PendingIntent.GetActivity(context, notificationId, openIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.IcPopupReminder)!
            .SetContentTitle(reminder.Title)!
            .SetContentText(reminder.Text)!
            .SetCategory(NotificationCompat.CategoryReminder)!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetAutoCancel(true)!
            .SetContentIntent(contentIntent)!
            .Build()!;
        notificationManager.Notify(notificationId, notification);
    }

    /// <summary>
    /// Reads the task that a tapped reminder notification opens, from local and pushed reminders alike.
    /// </summary>
    public static bool TryReadTarget(Intent? intent, out TaskNavigationRequest request)
    {
        request = null!;
        return intent is not null && TaskNavigationRequest.TryParse(intent.GetStringExtra(TaskIdExtra), intent.GetStringExtra(ListIdExtra), out request);
    }

    private static int GetNotificationId(Guid taskId) => taskId.GetHashCode();
}
