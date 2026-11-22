using AndroidX.Core.App;
using Errandesk.Client.Droid;
using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Contracts;
#if ERRANDESK_FIREBASE_MESSAGING
using Android.Gms.Extensions;
using Firebase.Messaging;
#endif

namespace Errandesk.Client.Services;

/// <summary>
/// Shows reminders on Android. When the app was built with Firebase configuration and the server sends Android pushes,
/// the device registers its Firebase Cloud Messaging token and the server pushes each reminder.
/// Otherwise the app sets an alarm per reminder that posts the notification, and sets the alarms again after a restart.
/// </summary>
public sealed class AndroidReminderScheduler : IReminderScheduler
{
    private const int MaximumScheduledCount = 200;

    private static event EventHandler<string>? s_pushTokenChanged;

    public string Platform => DevicePlatforms.Android;

    public event EventHandler<ReminderNotification>? InAppReminderRequested { add { } remove { } }

    public event EventHandler<string>? PushTokenChanged
    {
        add => s_pushTokenChanged += value;
        remove => s_pushTokenChanged -= value;
    }

    /// <summary>
    /// Reports a new Firebase Cloud Messaging token of this installation.
    /// </summary>
    public static void ReportPushToken(string pushToken) => s_pushTokenChanged?.Invoke(null, pushToken);

    public Task<string?> PrepareAsync(ServerInfoResponse? serverInfo, CancellationToken cancellationToken)
    {
        ReminderNotifications.EnsureChannel(global::Android.App.Application.Context, LocalizedStrings.Get("ReminderChannelName"));
        MainActivity.RequestNotificationPermission();
#if ERRANDESK_FIREBASE_MESSAGING
        if (serverInfo?.IsAndroidPushAvailable == true) return GetFirebaseTokenAsync();
#endif
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Sets alarms for the nearest reminders and cancels the alarms of reminders that are gone.
    /// </summary>
    public Task ScheduleAsync(IReadOnlyList<ReminderNotification> reminders, CancellationToken cancellationToken)
    {
        var context = global::Android.App.Application.Context;
        var store = new ReminderAlarmStore(context);
        var now = DateTimeOffset.UtcNow;
        List<ReminderNotification> nextReminders = [.. reminders.Where(reminder => reminder.ReminderAt > now).OrderBy(reminder => reminder.ReminderAt).Take(MaximumScheduledCount)];
        var nextTaskIds = nextReminders.Select(reminder => reminder.TaskId).ToHashSet();

        foreach (var previousReminder in store.Read().Where(reminder => !nextTaskIds.Contains(reminder.TaskId))) ReminderAlarms.Cancel(context, previousReminder.TaskId);
        foreach (var reminder in nextReminders) ReminderAlarms.Set(context, reminder);
        store.Write(nextReminders);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        var context = global::Android.App.Application.Context;
        var store = new ReminderAlarmStore(context);
        foreach (var reminder in store.Read()) ReminderAlarms.Cancel(context, reminder.TaskId);
        store.Write([]);
        NotificationManagerCompat.From(context)?.CancelAll();
        return Task.CompletedTask;
    }

#if ERRANDESK_FIREBASE_MESSAGING
    private static async Task<string?> GetFirebaseTokenAsync()
    {
        try
        {
            // The server sends to FCM registration tokens, which Firebase Messaging 26 marks obsolete in favour of installation IDs.
#pragma warning disable CS0618
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>();
#pragma warning restore CS0618
            var tokenText = token?.ToString();
            return string.IsNullOrEmpty(tokenText) ? null : tokenText;
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or InvalidOperationException) { return null; }
    }
#endif
}
