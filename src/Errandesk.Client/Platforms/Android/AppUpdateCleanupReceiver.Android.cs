using Android.Content;
using Errandesk.Client.Services;

namespace Errandesk.Client.Droid;

/// <summary>
/// Deletes the downloaded update APK once the update is installed.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[global::Android.App.IntentFilter([Intent.ActionMyPackageReplaced])]
public sealed class AppUpdateCleanupReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == Intent.ActionMyPackageReplaced)
        {
            ClientUpdateService.DeleteDownloadedPackages();
        }
    }
}
