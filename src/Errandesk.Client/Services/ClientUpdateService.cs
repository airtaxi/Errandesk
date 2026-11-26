using System.Globalization;
using System.Net.Http.Headers;
using Errandesk.Contracts;
#if WINDOWS
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
#else
using Errandesk.Client.Helpers;
#endif

namespace Errandesk.Client.Services;

/// <summary>
/// Reads the latest version from the update server, downloads the package of this platform and starts its installer.
/// The files are laid out as <see cref="ClientUpdateLayout"/> describes; Windows on an architecture other than ARM64 uses the x64 installer.
/// </summary>
public sealed class ClientUpdateService(HttpClient httpClient)
{
    private const int BufferSize = 81920;

    private static readonly TimeSpan s_versionCheckTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_downloadStallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient = httpClient;

    /// <summary>Whether this platform can install updates.</summary>
#if WINDOWS || ANDROID
    public static bool IsSupported => true;
#else
    public static bool IsSupported => false;
#endif

#if WINDOWS
    /// <summary>The version of the installed package, which the installer compares with the package it installs.</summary>
    public static Version CurrentVersion { get; } = ReadPackageVersion();

    private static string PlatformFolderName => ClientUpdateLayout.WindowsFolderName;

    private static string PackageFileName => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? ClientUpdateLayout.WindowsArm64InstallerFileName : ClientUpdateLayout.WindowsX64InstallerFileName;

    private static string DownloadDirectoryPath => Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Update");
#else
    /// <summary>The display version of this build with four components.</summary>
    public static Version CurrentVersion { get; } = ClientUpdateLayout.ParseVersion(DeviceInformationHelper.GetAppVersion()) ?? new Version(0, 0, 0, 0);

    private static string PlatformFolderName => ClientUpdateLayout.AndroidFolderName;

    private static string PackageFileName => ClientUpdateLayout.AndroidPackageFileName;

#if ANDROID
    private static string DownloadDirectoryPath => Droid.AppUpdateInstaller.DownloadDirectoryPath;
#else
    private static string DownloadDirectoryPath => throw new PlatformNotSupportedException();
#endif
#endif

    /// <summary>A version for display, without a trailing zero revision.</summary>
    public static string FormatVersion(Version version) => version.Revision == 0 ? version.ToString(3) : version.ToString();

    /// <summary>
    /// Returns the version on the update server when it is newer than this build.
    /// </summary>
    /// <exception cref="HttpRequestException">The version file could not be downloaded.</exception>
    /// <exception cref="InvalidDataException">The version file does not hold a version.</exception>
    /// <exception cref="IOException">The connection was closed while reading the version file.</exception>
    /// <exception cref="TimeoutException">The update server did not respond in time.</exception>
    public async Task<Version?> GetNewerVersionAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(s_versionCheckTimeout);
        try
        {
            using var request = CreateRequest(baseUrl, ClientUpdateLayout.VersionFileName);
            using var response = await _httpClient.SendAsync(request, timeoutCancellationTokenSource.Token);
            response.EnsureSuccessStatusCode();
            var versionText = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
            var latestVersion = ClientUpdateLayout.ParseVersion(versionText) ?? throw new InvalidDataException("The version file of the update server does not hold a version.");
            return latestVersion > CurrentVersion ? latestVersion : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The update server did not respond in time."); }
    }

    /// <summary>
    /// Downloads the package for this platform and architecture into the update folder and returns its path.
    /// </summary>
    /// <param name="progress">Receives the downloaded fraction from 0 to 1. Not reported when the server does not send the size.</param>
    /// <exception cref="HttpRequestException">The package could not be downloaded.</exception>
    /// <exception cref="InvalidDataException">The size of the downloaded package differs from the size the server sent.</exception>
    /// <exception cref="IOException">The connection was closed early or the file could not be written.</exception>
    /// <exception cref="TimeoutException">No data arrived for a while.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<string> DownloadPackageAsync(Uri baseUrl, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        DeleteDownloadedPackages();
        var packageFilePath = Path.Combine(DownloadDirectoryPath, PackageFileName);
        var temporaryFilePath = packageFilePath + ".tmp";
        Directory.CreateDirectory(DownloadDirectoryPath);
        try
        {
            await DownloadAsync(baseUrl, PackageFileName, temporaryFilePath, progress, cancellationToken);
            File.Move(temporaryFilePath, packageFilePath, true);
        }
        catch
        {
            TryDeleteFile(temporaryFilePath);
            throw;
        }

        return packageFilePath;
    }

#if WINDOWS
    /// <summary>
    /// Starts the installer and exits so the installer can replace the package of this app.
    /// The shell starts the installer outside the package, so the installer keeps running while it closes this app.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The shell could not be started.</exception>
    [DoesNotReturn]
    public static void RunInstallerAndExit(string installerFilePath)
    {
        var processStartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        processStartInfo.ArgumentList.Add(installerFilePath);
        Process.Start(processStartInfo);
        Environment.Exit(0);
    }
#else
    /// <summary>
    /// Opens the system package installer for a downloaded package, after the user allows this app to install apps when it may not yet.
    /// </summary>
    /// <returns><see langword="false"/> when the user did not allow this app to install apps.</returns>
    /// <exception cref="InvalidOperationException">The package installer could not be opened.</exception>
#if ANDROID
    public static Task<bool> OpenInstallerAsync(string packageFilePath) => Droid.AppUpdateInstaller.InstallAsync(packageFilePath);
#else
    public static Task<bool> OpenInstallerAsync(string packageFilePath) => throw new PlatformNotSupportedException();
#endif
#endif

    /// <summary>
    /// Removes the packages of an earlier update. A package that is still in use is left for the next launch.
    /// </summary>
    public static void DeleteDownloadedPackages()
    {
        if (!IsSupported || !Directory.Exists(DownloadDirectoryPath)) return;

        try { Directory.Delete(DownloadDirectoryPath, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

#if WINDOWS
    private static Version ReadPackageVersion()
    {
        var packageVersion = global::Windows.ApplicationModel.Package.Current.Id.Version;
        return new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
    }
#endif

    private async Task DownloadAsync(Uri baseUrl, string fileName, string filePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // Every read restarts the stall timer, so only a download that stops receiving data times out.
        using var stallCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stallCancellationTokenSource.CancelAfter(s_downloadStallTimeout);
        try
        {
            using var request = CreateRequest(baseUrl, fileName);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stallCancellationTokenSource.Token);
            response.EnsureSuccessStatusCode();
            var totalByteCount = response.Content.Headers.ContentLength ?? 0;
            await using var responseStream = await response.Content.ReadAsStreamAsync(stallCancellationTokenSource.Token);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);

            var buffer = new byte[BufferSize];
            var downloadedByteCount = 0L;
            int readCount;
            progress?.Report(0);
            while ((readCount = await responseStream.ReadAsync(buffer, stallCancellationTokenSource.Token)) > 0)
            {
                stallCancellationTokenSource.CancelAfter(s_downloadStallTimeout);
                await fileStream.WriteAsync(buffer.AsMemory(0, readCount), stallCancellationTokenSource.Token);
                downloadedByteCount += readCount;
                if (totalByteCount > 0) progress?.Report(Math.Min(1, (double)downloadedByteCount / totalByteCount));
            }

            if (totalByteCount > 0 && downloadedByteCount != totalByteCount) throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The downloaded package has {downloadedByteCount} bytes instead of {totalByteCount}."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The update server stopped sending the package."); }
    }

    /// <summary>
    /// Creates a request for a file in the platform folder. The files are replaced in place on the update server, so cached copies are not used.
    /// </summary>
    private static HttpRequestMessage CreateRequest(Uri baseUrl, string fileName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ClientUpdateLayout.GetFileUri(baseUrl, PlatformFolderName, fileName));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    private static void TryDeleteFile(string filePath)
    {
        try { File.Delete(filePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
