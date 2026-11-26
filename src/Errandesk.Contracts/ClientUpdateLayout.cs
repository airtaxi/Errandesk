namespace Errandesk.Contracts;

/// <summary>
/// How client updates are published below the client update base URL: a folder per platform with a <c>version</c> file that holds the latest version,
/// next to the packages of that version.
/// </summary>
public static class ClientUpdateLayout
{
    public const string VersionFileName = "version";
    public const string WindowsFolderName = "windows";
    public const string AndroidFolderName = "android";
    public const string WindowsX64InstallerFileName = "Installer-x64.exe";
    public const string WindowsArm64InstallerFileName = "Installer-arm64.exe";
    public const string AndroidPackageFileName = "Errandesk-latest.apk";

    /// <summary>
    /// Returns <paramref name="value"/> as a base URL that ends with a slash, so relative paths resolve below it.
    /// </summary>
    /// <returns><see langword="null"/> when the value is not an absolute <c>http</c> or <c>https</c> URL, or has a query or fragment.</returns>
    public static Uri? NormalizeBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var baseUrl)) return null;
        if (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment)) return null;
        return baseUrl.AbsolutePath.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
    }

    /// <summary>
    /// The address of <paramref name="fileName"/> in the folder of a platform. Spaces and other reserved characters in the file name are escaped.
    /// </summary>
    /// <param name="baseUrl">A base URL returned by <see cref="NormalizeBaseUrl"/>.</param>
    public static Uri GetFileUri(Uri baseUrl, string platformFolderName, string fileName) => new(new Uri(baseUrl, Uri.EscapeDataString(platformFolderName) + "/"), Uri.EscapeDataString(fileName));

    /// <summary>
    /// Parses the content of a <c>version</c> file. Missing components count as zero, so <c>1.2</c>, <c>1.2.0</c> and <c>1.2.0.0</c> are equal,
    /// and a pre-release or build suffix after <c>-</c> or <c>+</c> is ignored.
    /// </summary>
    /// <returns>A version with four components, or <see langword="null"/> when the text is not a version.</returns>
    public static Version? ParseVersion(string? text)
    {
        var versionText = text?.Trim() ?? string.Empty;
        var suffixIndex = versionText.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) versionText = versionText[..suffixIndex];
        if (!Version.TryParse(versionText, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
