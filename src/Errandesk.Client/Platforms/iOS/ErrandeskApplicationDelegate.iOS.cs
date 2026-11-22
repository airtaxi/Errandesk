using Errandesk.Client.Services;
using Foundation;
using UIKit;
using UserNotifications;

namespace Errandesk.Client.iOS;

/// <summary>
/// Handles the notification parts of the application lifecycle: the notification center delegate and the APNs device token.
/// </summary>
public class ErrandeskApplicationDelegate : Uno.UI.Runtime.AppleUIKit.UnoUIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        UNUserNotificationCenter.Current.Delegate = ReminderNotificationCenterDelegate.Instance;
        return base.FinishedLaunching(application, launchOptions);
    }

    public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken) => AppleReminderScheduler.ReportDeviceToken(Convert.ToHexStringLower(deviceToken.ToArray()));

    public override void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error) => AppleReminderScheduler.ReportDeviceTokenFailure();
}
