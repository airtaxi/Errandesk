using Errandesk.Client.Models;
using Errandesk.Contracts;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Errandesk.Client.Services;

/// <summary>
/// Schedules reminders as scheduled app notifications of the packaged app, which Windows shows even while the app is closed.
/// Clicking a notification activates the app through <see cref="WindowsNotificationActivation"/>.
/// </summary>
public sealed class WindowsReminderScheduler : IReminderScheduler
{
    /// <summary>The notification argument that carries the task ID.</summary>
    public const string TaskIdArgument = "taskId";

    /// <summary>The notification argument that carries the list ID.</summary>
    public const string ListIdArgument = "listId";

    private const string ReminderGroup = "reminders";
    private const int MaximumScheduledCount = 500;
    private static readonly TimeSpan s_minimumLeadTime = TimeSpan.FromSeconds(5);

    public string Platform => DevicePlatforms.Windows;

    public event EventHandler<ReminderNotification>? InAppReminderRequested { add { } remove { } }

    public event EventHandler<string>? PushTokenChanged { add { } remove { } }

    public Task<string?> PrepareAsync(ServerInfoResponse? serverInfo, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>
    /// Keeps scheduled notifications whose time and content are unchanged, replaces changed ones and removes the rest.
    /// Windows refuses notifications scheduled in the past, so only reminders a few seconds ahead are scheduled.
    /// </summary>
    public Task ScheduleAsync(IReadOnlyList<ReminderNotification> reminders, CancellationToken cancellationToken)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            var earliestTime = DateTimeOffset.UtcNow + s_minimumLeadTime;
            var remindersByTag = reminders.Where(reminder => reminder.ReminderAt > earliestTime).OrderBy(reminder => reminder.ReminderAt).Take(MaximumScheduledCount).ToDictionary(reminder => CreateTag(reminder.TaskId));

            foreach (var scheduledNotification in notifier.GetScheduledToastNotifications().Where(notification => notification.Group == ReminderGroup))
            {
                if (remindersByTag.TryGetValue(scheduledNotification.Tag, out var reminder) && scheduledNotification.DeliveryTime == reminder.ReminderAt && scheduledNotification.Content.GetXml() == CreateContent(reminder).GetXml())
                {
                    remindersByTag.Remove(scheduledNotification.Tag);
                    continue;
                }

                notifier.RemoveFromSchedule(scheduledNotification);
            }

            foreach (var reminder in remindersByTag.Values) notifier.AddToSchedule(new ScheduledToastNotification(CreateContent(reminder), reminder.ReminderAt) { Tag = CreateTag(reminder.TaskId), Group = ReminderGroup });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var scheduledNotification in notifier.GetScheduledToastNotifications().Where(notification => notification.Group == ReminderGroup)) notifier.RemoveFromSchedule(scheduledNotification);
            ToastNotificationManager.History.RemoveGroup(ReminderGroup);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }

        return Task.CompletedTask;
    }

    private static string CreateTag(Guid taskId) => taskId.ToString("N");

    private static XmlDocument CreateContent(ReminderNotification reminder)
    {
        var payload = new AppNotificationBuilder().AddArgument(TaskIdArgument, reminder.TaskId.ToString()).AddArgument(ListIdArgument, reminder.ListId.ToString()).AddText(reminder.Title).AddText(reminder.Text).BuildNotification().Payload;
        var document = new XmlDocument();
        document.LoadXml(payload);
        return document;
    }
}
