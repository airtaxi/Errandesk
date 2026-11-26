using Android.Content;
using AndroidX.Core.Content;

namespace Errandesk.Client.Droid;

/// <summary>
/// Hands a downloaded update APK to the system package installer. The APK is kept in the <c>updates</c> folder of the app cache,
/// which the FileProvider declared in the manifest shares through <c>Resources/xml/app_update_file_paths.xml</c>.
/// </summary>
public static class AppUpdateInstaller
{
    private const string ApkMimeType = "application/vnd.android.package-archive";
    private const string FileProviderAuthoritySuffix = ".updates";

    /// <summary>The folder that holds downloaded update APKs.</summary>
    public static string DownloadDirectoryPath => Path.Combine(Android.App.Application.Context.CacheDir!.AbsolutePath, "updates");

    /// <summary>
    /// Opens the system package installer for <paramref name="apkFilePath"/>. On Android 8 and later the user first allows this app to install apps when it may not yet.
    /// </summary>
    /// <returns><see langword="false"/> when the user did not allow this app to install apps.</returns>
    /// <exception cref="InvalidOperationException">No app on the device installs packages.</exception>
    public static async Task<bool> InstallAsync(string apkFilePath)
    {
        var context = Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && context.PackageManager?.CanRequestPackageInstalls() != true && !await MainActivity.RequestPackageInstallPermissionAsync()) return false;

        var apkUri = FileProvider.GetUriForFile(context, context.PackageName + FileProviderAuthoritySuffix, new Java.IO.File(apkFilePath));
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(apkUri, ApkMimeType);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        try { context.StartActivity(intent); }
        catch (ActivityNotFoundException exception) { throw new InvalidOperationException("No app on this device installs packages.", exception); }

        return true;
    }
}
