namespace Errandesk.Server.Notifications;

/// <summary>
/// A visible push notification with data for the app.
/// </summary>
/// <param name="Title">Notification title.</param>
/// <param name="Body">Notification text.</param>
/// <param name="Data">Key-value pairs delivered to the app with the notification.</param>
public sealed record PushNotification(string Title, string Body, IReadOnlyDictionary<string, string> Data);
