using Errandesk.Contracts;

namespace Errandesk.Server.Notifications;

/// <summary>
/// The text and data of reminder notifications.
/// </summary>
public static class ReminderNotificationText
{
    public const string TaskIdKey = "taskId";
    public const string ListIdKey = "listId";

    /// <summary>The notification body in the user's language, English for unknown languages.</summary>
    public static string GetBody(string language) => language == UserLanguages.Korean ? "할 일을 확인할 시간입니다." : "It's time for this task.";

    public static PushNotification Create(string title, string language, Guid taskId, Guid listId) => new(title, GetBody(language), new Dictionary<string, string> { [TaskIdKey] = taskId.ToString(), [ListIdKey] = listId.ToString() });
}
