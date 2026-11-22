using Errandesk.Client.Models;
using Errandesk.Contracts;
using Foundation;
using UIKit;
using UserNotifications;

namespace Errandesk.Client.Services;

/// <summary>
/// Shows reminders on iOS. After the user allows notifications, the device registers for remote notifications when the server
/// sends iOS pushes, and the server pushes each reminder through APNs. Otherwise the app schedules local notifications for the
/// nearest 64 reminders, the most iOS keeps pending for an app.
/// </summary>
public sealed class AppleReminderScheduler : IReminderScheduler
{
    /// <summary>The user info key that carries the task ID, as in pushed reminders.</summary>
    public const string TaskIdKey = "taskId";

    /// <summary>The user info key that carries the list ID, as in pushed reminders.</summary>
    public const string ListIdKey = "listId";

    private const string IdentifierPrefix = "reminder-";
    private const int MaximumScheduledCount = 64;
    private static readonly TimeSpan s_deviceTokenTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan s_minimumLeadTime = TimeSpan.FromSeconds(1);

    private static TaskCompletionSource<string?>? s_deviceTokenCompletion;
    private static event EventHandler<string>? s_pushTokenChanged;

    public string Platform => DevicePlatforms.Ios;

    public event EventHandler<ReminderNotification>? InAppReminderRequested { add { } remove { } }

    public event EventHandler<string>? PushTokenChanged
    {
        add => s_pushTokenChanged += value;
        remove => s_pushTokenChanged -= value;
    }

    /// <summary>
    /// Reports the APNs device token, in hexadecimal, from the application delegate.
    /// </summary>
    public static void ReportDeviceToken(string deviceToken)
    {
        if (s_deviceTokenCompletion is { } completion && completion.TrySetResult(deviceToken)) return;
        s_pushTokenChanged?.Invoke(null, deviceToken);
    }

    public static void ReportDeviceTokenFailure() => s_deviceTokenCompletion?.TrySetResult(null);

    public async Task<string?> PrepareAsync(ServerInfoResponse? serverInfo, CancellationToken cancellationToken)
    {
        bool isGranted;
        try { (isGranted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound); }
        catch (NSErrorException) { return null; }
        if (!isGranted || serverInfo?.IsIosPushAvailable != true) return null;

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_deviceTokenCompletion = completion;
        UIApplication.SharedApplication.InvokeOnMainThread(UIApplication.SharedApplication.RegisterForRemoteNotifications);
        var finishedTask = await Task.WhenAny(completion.Task, Task.Delay(s_deviceTokenTimeout, cancellationToken));
        return finishedTask == completion.Task ? await completion.Task : null;
    }

    /// <summary>
    /// Replaces the pending reminder notifications with the nearest reminders. A request with an existing identifier replaces that request.
    /// </summary>
    public async Task ScheduleAsync(IReadOnlyList<ReminderNotification> reminders, CancellationToken cancellationToken)
    {
        var notificationCenter = UNUserNotificationCenter.Current;
        var now = DateTimeOffset.UtcNow;
        List<ReminderNotification> nextReminders = [.. reminders.Where(reminder => reminder.ReminderAt - now >= s_minimumLeadTime).OrderBy(reminder => reminder.ReminderAt).Take(MaximumScheduledCount)];
        var nextIdentifiers = nextReminders.Select(reminder => CreateIdentifier(reminder.TaskId)).ToHashSet();

        var pendingRequests = await notificationCenter.GetPendingNotificationRequestsAsync();
        var staleIdentifiers = pendingRequests.Select(request => request.Identifier).Where(identifier => identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal) && !nextIdentifiers.Contains(identifier)).ToArray();
        if (staleIdentifiers.Length > 0) notificationCenter.RemovePendingNotificationRequests(staleIdentifiers);

        foreach (var reminder in nextReminders)
        {
            var content = new UNMutableNotificationContent
            {
                Title = reminder.Title,
                Body = reminder.Text,
                Sound = UNNotificationSound.Default,
                UserInfo = NSDictionary.FromObjectsAndKeys([new NSString(reminder.TaskId.ToString()), new NSString(reminder.ListId.ToString())], [new NSString(TaskIdKey), new NSString(ListIdKey)]),
            };
            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(Math.Max(s_minimumLeadTime.TotalSeconds, (reminder.ReminderAt - DateTimeOffset.UtcNow).TotalSeconds), false);
            try { await notificationCenter.AddNotificationRequestAsync(UNNotificationRequest.FromIdentifier(CreateIdentifier(reminder.TaskId), content, trigger)); }
            catch (NSErrorException) { }
        }
    }

    public async Task ClearAsync()
    {
        var notificationCenter = UNUserNotificationCenter.Current;
        var pendingRequests = await notificationCenter.GetPendingNotificationRequestsAsync();
        notificationCenter.RemovePendingNotificationRequests([.. pendingRequests.Select(request => request.Identifier).Where(identifier => identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal))]);
        notificationCenter.RemoveAllDeliveredNotifications();
    }

    private static string CreateIdentifier(Guid taskId) => $"{IdentifierPrefix}{taskId:N}";
}
