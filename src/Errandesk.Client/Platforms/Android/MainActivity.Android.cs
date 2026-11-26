using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Errandesk.Client.Droid;

[Activity(MainLauncher = true, ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges, WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden, Exported = true)]
public class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    /// <summary>The intent action that opens the assistant pane, which the home screen widget sends.</summary>
    public const string OpenAssistantAction = "errandesk.OPEN_ASSISTANT";

    private const int NotificationPermissionRequestCode = 1001;

    private static MainActivity? s_runningActivity;

    /// <summary>
    /// Asks for permission to post notifications, which Android 13 and later require. Does nothing when it is granted already.
    /// </summary>
    public static void RequestNotificationPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || s_runningActivity is not { } activity) return;
        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications) == Permission.Granted) return;

        ActivityCompat.RequestPermissions(activity, [Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        s_runningActivity = this;
        HandleLaunchIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleLaunchIntent(intent);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(s_runningActivity, this)) s_runningActivity = null;
        base.OnDestroy();
    }

    /// <summary>
    /// Opens the assistant pane or the task an intent asks for, then removes the request from the intent so it is handled only once.
    /// </summary>
    private static void HandleLaunchIntent(Intent? intent)
    {
        if (intent?.Action == OpenAssistantAction)
        {
            intent.SetAction(null);
            App.OpenAssistant();
            return;
        }

        OpenNotificationTask(intent);
    }

    /// <summary>
    /// Opens the task of a tapped reminder notification, then removes the task from the intent so it opens only once.
    /// </summary>
    private static void OpenNotificationTask(Intent? intent)
    {
        if (!ReminderNotifications.TryReadTarget(intent, out var request)) return;

        intent!.RemoveExtra(ReminderNotifications.TaskIdExtra);
        intent.RemoveExtra(ReminderNotifications.ListIdExtra);
        App.OpenTaskFromNotification(request);
    }
}
