#if ERRANDESK_FIREBASE_MESSAGING
using Android.App;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Firebase.Messaging;

namespace Errandesk.Client.Droid;

/// <summary>
/// Receives pushed reminders and token changes from Firebase Cloud Messaging.
/// While the app is in the background the system shows pushed reminders itself; in the foreground this service posts them.
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ReminderFirebaseMessagingService : FirebaseMessagingService
{
    // Registration tokens, which the server sends to, are marked obsolete in favour of installation IDs.
#pragma warning disable CS0672
    public override void OnNewToken(string token) => AndroidReminderScheduler.ReportPushToken(token);
#pragma warning restore CS0672

    public override void OnMessageReceived(RemoteMessage message)
    {
        if (message.GetNotification() is not { } notification) return;

        message.Data.TryGetValue(ReminderNotifications.TaskIdExtra, out var taskIdText);
        message.Data.TryGetValue(ReminderNotifications.ListIdExtra, out var listIdText);
        if (!TaskNavigationRequest.TryParse(taskIdText, listIdText, out var target)) return;

        ReminderNotifications.Post(this, new ReminderNotification(target.TaskId, target.ListId ?? Guid.Empty, notification.Title ?? string.Empty, notification.Body ?? string.Empty, DateTimeOffset.UtcNow));
    }
}
#endif
