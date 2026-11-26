using System.ComponentModel;
using Errandesk.Client.Models;
using Errandesk.Client.Services;

namespace Errandesk.Client.Managers;

/// <summary>
/// Checks for and installs app updates for the whole app, so a download keeps running and its state stays available while the user moves between pages.
/// Every member is used on the UI thread, and <see cref="StateChanged"/> is raised there.
/// </summary>
public sealed class ClientUpdateManager(ClientUpdateService clientUpdateService)
{
    private readonly ClientUpdateService _clientUpdateService = clientUpdateService;
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private Uri? _baseUrl;
    private string? _downloadedPackageFilePath;

    /// <summary>Raised when <see cref="State"/>, <see cref="DownloadPercent"/> or the result of the last check changes.</summary>
    public event EventHandler? StateChanged;

    public ClientUpdateState State { get; private set; } = ClientUpdateState.Idle;

    /// <summary>The newer version found by the last check, or <see langword="null"/>.</summary>
    public Version? NewerVersion { get; private set; }

    /// <summary>The downloaded share of the package from 0 to 100 while <see cref="State"/> is <see cref="ClientUpdateState.Downloading"/>.</summary>
    public int DownloadPercent { get; private set; }

    /// <summary>The failure of the last check or installation, or <see langword="null"/>.</summary>
    public Exception? LastError { get; private set; }

    private bool IsBusy => State is ClientUpdateState.Checking or ClientUpdateState.Downloading;

    /// <summary>
    /// Asks the update server below <paramref name="baseUrl"/> for a version newer than this build. Does nothing while a check or download runs.
    /// </summary>
    public async Task CheckAsync(Uri baseUrl)
    {
        if (IsBusy) return;

        if (_baseUrl != baseUrl) _downloadedPackageFilePath = null;
        _baseUrl = baseUrl;
        LastError = null;
        SetState(ClientUpdateState.Checking);
        try
        {
            var newerVersion = await _clientUpdateService.GetNewerVersionAsync(baseUrl, CancellationToken.None);
            if (newerVersion != NewerVersion) _downloadedPackageFilePath = null;
            NewerVersion = newerVersion;
            SetState(newerVersion is null ? ClientUpdateState.UpToDate : ClientUpdateState.UpdateAvailable);
        }
        catch (Exception exception) when (IsUpdateFailure(exception))
        {
            NewerVersion = null;
            LastError = exception;
            SetState(ClientUpdateState.CheckFailed);
        }
    }

    /// <summary>
    /// Downloads the version found by the last check, unless it is downloaded already, and installs it.
    /// On Windows the app exits while the installer runs; on Android the system package installer opens.
    /// </summary>
    public async Task InstallAsync()
    {
        if (IsBusy || _baseUrl is not { } baseUrl || NewerVersion is null) return;

        using var downloadCancellationTokenSource = new CancellationTokenSource();
        _downloadCancellationTokenSource = downloadCancellationTokenSource;
        LastError = null;
        try
        {
            if (_downloadedPackageFilePath is null || !File.Exists(_downloadedPackageFilePath))
            {
                DownloadPercent = 0;
                SetState(ClientUpdateState.Downloading);
                _downloadedPackageFilePath = await _clientUpdateService.DownloadPackageAsync(baseUrl, new Progress<double>(OnDownloadProgress), downloadCancellationTokenSource.Token);
            }

#if WINDOWS
            ClientUpdateService.RunInstallerAndExit(_downloadedPackageFilePath);
#else
            SetState(await ClientUpdateService.OpenInstallerAsync(_downloadedPackageFilePath) ? ClientUpdateState.Installing : ClientUpdateState.PermissionRequired);
#endif
        }
        catch (OperationCanceledException) when (downloadCancellationTokenSource.IsCancellationRequested) { SetState(ClientUpdateState.Idle); }
        catch (Exception exception) when (IsUpdateFailure(exception))
        {
            LastError = exception;
            SetState(ClientUpdateState.InstallFailed);
        }
        finally { _downloadCancellationTokenSource = null; }
    }

    /// <summary>
    /// Hides the result of the last check or installation, and cancels a running download. The newer version found by the last check is kept.
    /// </summary>
    public void Dismiss()
    {
        if (State == ClientUpdateState.Downloading)
        {
            _downloadCancellationTokenSource?.Cancel();
            return;
        }

        if (State != ClientUpdateState.Checking) SetState(ClientUpdateState.Idle);
    }

    /// <summary>
    /// Forgets the result of the last check and cancels a running download, for example when the app connects to a server that publishes updates elsewhere.
    /// </summary>
    public void Reset()
    {
        _downloadCancellationTokenSource?.Cancel();
        _baseUrl = null;
        _downloadedPackageFilePath = null;
        NewerVersion = null;
        LastError = null;
        if (State is not (ClientUpdateState.Checking or ClientUpdateState.Downloading)) SetState(ClientUpdateState.Idle);
    }

    /// <summary>Whether <paramref name="baseUrl"/> is the update server of the last check.</summary>
    public bool IsCheckedAgainst(Uri baseUrl) => _baseUrl == baseUrl;

    private static bool IsUpdateFailure(Exception exception) => exception is HttpRequestException or IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or Win32Exception;

    /// <summary>Progress reports are posted to the UI thread, so a report that arrives after the download has ended is ignored.</summary>
    private void OnDownloadProgress(double fraction)
    {
        var percent = (int)(fraction * 100);
        if (State != ClientUpdateState.Downloading || percent == DownloadPercent) return;

        DownloadPercent = percent;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(ClientUpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
