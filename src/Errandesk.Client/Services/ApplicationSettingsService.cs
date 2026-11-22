using Errandesk.Client.Models;
using Windows.Storage;

namespace Errandesk.Client.Services;

/// <summary>
/// Reads and writes the preferences kept in the application's local settings.
/// </summary>
public sealed class ApplicationSettingsService
{
    private const string ServerAddressKey = "ServerAddress";
    private const string ThemePreferenceKey = "ThemePreference";
    private const string LanguagePreferenceKey = "LanguagePreference";
    private const string DeviceInstallationIdKey = "DeviceInstallationId";
    private const string SignedInUserKey = "SignedInUser";
    private const string DismissedBriefingIdKey = "DismissedBriefingId";

    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    /// <summary>
    /// The ID this installation registers with on the server, created on first use.
    /// </summary>
    public Guid DeviceInstallationId
    {
        get
        {
            if (Guid.TryParse(GetString(DeviceInstallationIdKey), out var installationId) && installationId != Guid.Empty) return installationId;

            installationId = Guid.CreateVersion7();
            SetString(DeviceInstallationIdKey, installationId.ToString());
            return installationId;
        }
    }

    /// <summary>
    /// The absolute address of the Errandesk server, or <see langword="null"/> when none is saved.
    /// </summary>
    public string? ServerAddress
    {
        get => GetString(ServerAddressKey);
        set => SetString(ServerAddressKey, value);
    }

    /// <summary>
    /// The profile of the signed-in user as JSON, kept so the app can open with the data stored on this device while the server cannot be reached.
    /// </summary>
    public string? SignedInUser
    {
        get => GetString(SignedInUserKey);
        set => SetString(SignedInUserKey, value);
    }

    /// <summary>
    /// The ID of the morning briefing the user dismissed on this device, or <see langword="null"/>.
    /// </summary>
    public Guid? DismissedBriefingId
    {
        get => Guid.TryParse(GetString(DismissedBriefingIdKey), out var briefingId) ? briefingId : null;
        set => SetString(DismissedBriefingIdKey, value?.ToString());
    }

    public ThemePreference ThemePreference
    {
        get => GetInteger(ThemePreferenceKey) is var value && Enum.IsDefined((ThemePreference)value) ? (ThemePreference)value : ThemePreference.System;
        set => _localSettings.Values[ThemePreferenceKey] = (int)value;
    }

    public LanguagePreference LanguagePreference
    {
        get => GetInteger(LanguagePreferenceKey) is var value && Enum.IsDefined((LanguagePreference)value) ? (LanguagePreference)value : LanguagePreference.System;
        set => _localSettings.Values[LanguagePreferenceKey] = (int)value;
    }

    private string? GetString(string key) => _localSettings.Values.TryGetValue(key, out var value) ? value as string : null;

    private int GetInteger(string key) => _localSettings.Values.TryGetValue(key, out var value) && value is int integer ? integer : 0;

    private void SetString(string key, string? value)
    {
        if (value is null) _localSettings.Values.Remove(key);
        else _localSettings.Values[key] = value;
    }
}
