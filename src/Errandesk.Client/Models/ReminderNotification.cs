namespace Errandesk.Client.Models;

/// <summary>
/// A reminder that this device shows as a notification.
/// </summary>
/// <param name="TaskId">The task the reminder belongs to.</param>
/// <param name="ListId">The list that contains the task.</param>
/// <param name="Title">The notification title, which is the task title.</param>
/// <param name="Text">The notification body.</param>
/// <param name="ReminderAt">When the notification is due.</param>
public sealed record ReminderNotification(Guid TaskId, Guid ListId, string Title, string Text, DateTimeOffset ReminderAt);
