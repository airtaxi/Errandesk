using Errandesk.Client.Models;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace Errandesk.Client.Services;

/// <summary>
/// Opens the task of a reminder notification the user clicked, whether the click started the app or reached it while running.
/// </summary>
public static class WindowsNotificationActivation
{
    /// <summary>
    /// Registers the app as the target of its notifications and handles a notification that started it. Call once at launch.
    /// </summary>
    public static void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnAppNotificationManagerNotificationInvoked;
            AppNotificationManager.Default.Register();

            var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedEventArgs.Kind == ExtendedActivationKind.AppNotification && activatedEventArgs.Data is AppNotificationActivatedEventArgs notificationEventArgs) OpenTask(notificationEventArgs);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static void OnAppNotificationManagerNotificationInvoked(AppNotificationManager _, AppNotificationActivatedEventArgs eventArgs) => OpenTask(eventArgs);

    private static void OpenTask(AppNotificationActivatedEventArgs eventArgs)
    {
        eventArgs.Arguments.TryGetValue(WindowsReminderScheduler.TaskIdArgument, out var taskIdText);
        eventArgs.Arguments.TryGetValue(WindowsReminderScheduler.ListIdArgument, out var listIdText);
        if (TaskNavigationRequest.TryParse(taskIdText, listIdText, out var request)) App.OpenTaskFromNotification(request);
    }
}
