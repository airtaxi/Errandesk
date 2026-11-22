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
        OpenNotificationTask(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenNotificationTask(intent);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(s_runningActivity, this)) s_runningActivity = null;
        base.OnDestroy();
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
