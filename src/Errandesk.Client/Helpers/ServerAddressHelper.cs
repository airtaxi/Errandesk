using System.Diagnostics.CodeAnalysis;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Parses and describes server addresses entered by the user.
/// </summary>
public static class ServerAddressHelper
{
    /// <summary>
    /// Turns user input into an absolute http or https address whose path ends with a slash, without query or fragment.
    /// Input without a scheme is treated as https.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out Uri? serverAddress)
    {
        serverAddress = null;
        var trimmedInput = input?.Trim();
        if (string.IsNullOrEmpty(trimmedInput)) return false;
        if (!trimmedInput.Contains("://", StringComparison.Ordinal)) trimmedInput = $"https://{trimmedInput}";
        if (!Uri.TryCreate(trimmedInput, UriKind.Absolute, out var parsedAddress)) return false;
        if (parsedAddress.Scheme != Uri.UriSchemeHttp && parsedAddress.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(parsedAddress.Host)) return false;

        var path = parsedAddress.AbsolutePath.EndsWith('/') ? parsedAddress.AbsolutePath : $"{parsedAddress.AbsolutePath}/";
        serverAddress = new UriBuilder(parsedAddress.Scheme, parsedAddress.Host, parsedAddress.Port, path).Uri;
        return true;
    }

    /// <summary>
    /// Whether the input is a valid address that sends data unencrypted to a host other than this device.
    /// </summary>
    public static bool IsUnencryptedRemoteAddress(string? input) => TryNormalize(input, out var serverAddress) && serverAddress.Scheme == Uri.UriSchemeHttp && !serverAddress.IsLoopback;

    /// <summary>
    /// Formats a normalized address for display, without the trailing slash.
    /// </summary>
    public static string ToDisplayText(Uri serverAddress) => serverAddress.AbsoluteUri.TrimEnd('/');
}
