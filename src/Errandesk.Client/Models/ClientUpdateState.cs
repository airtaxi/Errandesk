namespace Errandesk.Client.Models;

/// <summary>
/// The stage of checking for and installing an app update.
/// </summary>
public enum ClientUpdateState
{
    /// <summary>No result is shown.</summary>
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    CheckFailed,
    Downloading,
    /// <summary>The installer was started; on Android the system package installer is open.</summary>
    Installing,
    /// <summary>The user did not allow the app to install apps.</summary>
    PermissionRequired,
    InstallFailed,
}
