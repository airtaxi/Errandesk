using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Errandesk.Contracts;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Describes this device and its regional settings for registration and sign-in.
/// </summary>
public static class DeviceInformationHelper
{
    private const int MaximumDeviceNameLength = 100;
    private const string AppVersionMetadataKey = "ErrandeskAppVersion";

    /// <summary>
    /// A name for this device that identifies the session on the server.
    /// </summary>
    public static string GetDeviceName()
    {
        string? machineName = null;
        try { machineName = Environment.MachineName; }
        catch (InvalidOperationException) { }

        var deviceName = string.IsNullOrWhiteSpace(machineName) || machineName.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? RuntimeInformation.OSDescription : machineName;
        return deviceName.Length > MaximumDeviceNameLength ? deviceName[..MaximumDeviceNameLength] : deviceName;
    }

    /// <summary>
    /// The display version of the installed app, from the assembly metadata the build writes.
    /// </summary>
    public static string GetAppVersion() => typeof(DeviceInformationHelper).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == AppVersionMetadataKey)?.Value ?? string.Empty;

    /// <summary>
    /// The IANA ID of this device's time zone, or <c>UTC</c> when it has none.
    /// </summary>
    public static string GetIanaTimeZoneId()
    {
        var localTimeZone = TimeZoneInfo.Local;
        if (localTimeZone.HasIanaId) return localTimeZone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(localTimeZone.Id, out var ianaTimeZoneId) ? ianaTimeZoneId : "UTC";
    }

    /// <summary>
    /// The supported language that matches the current interface culture: <c>ko</c> for Korean, otherwise <c>en</c>.
    /// </summary>
    public static string GetInterfaceLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == UserLanguages.Korean ? UserLanguages.Korean : UserLanguages.English;
}
