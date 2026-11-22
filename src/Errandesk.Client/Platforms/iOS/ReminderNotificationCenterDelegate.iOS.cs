using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Foundation;
using UserNotifications;

namespace Errandesk.Client.iOS;

/// <summary>
/// Shows reminders that arrive while the app is in the foreground and opens the task of a tapped reminder.
/// </summary>
public sealed class ReminderNotificationCenterDelegate : UNUserNotificationCenterDelegate
{
    public static ReminderNotificationCenterDelegate Instance { get; } = new();

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        var userInfo = response.Notification.Request.Content.UserInfo;
        var taskIdText = userInfo.ObjectForKey(new NSString(AppleReminderScheduler.TaskIdKey))?.ToString();
        var listIdText = userInfo.ObjectForKey(new NSString(AppleReminderScheduler.ListIdKey))?.ToString();
        if (response.IsDefaultAction && TaskNavigationRequest.TryParse(taskIdText, listIdText, out var request)) App.OpenTaskFromNotification(request);
        completionHandler();
    }
}
