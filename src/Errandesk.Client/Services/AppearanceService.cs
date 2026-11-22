using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Views;
using Errandesk.Contracts;
using Windows.Globalization;

namespace Errandesk.Client.Services;

/// <summary>
/// Applies the theme and display language preferences.
/// </summary>
public sealed class AppearanceService(ApplicationSettingsService settingsService)
{
    /// <summary>
    /// Applies the saved language preference. Call it at startup, before any localized resource is read.
    /// </summary>
    public void ApplySavedLanguage() => ApplyLanguage(settingsService.LanguagePreference);

    public void ApplySavedTheme() => MainWindow.SetRequestedTheme(ToElementTheme(settingsService.ThemePreference));

    public void SetTheme(ThemePreference themePreference)
    {
        settingsService.ThemePreference = themePreference;
        MainWindow.SetRequestedTheme(ToElementTheme(themePreference));
    }

    /// <summary>
    /// Saves the language preference and sets the language override. Pages that are already shown keep their language until the app restarts.
    /// </summary>
    public void SetLanguage(LanguagePreference languagePreference)
    {
        settingsService.LanguagePreference = languagePreference;
        ApplyLanguage(languagePreference);
    }

    /// <summary>
    /// The language to store on the server for <paramref name="languagePreference"/>: the chosen language, or the system language when the preference follows the system.
    /// </summary>
    public static string GetUserLanguage(LanguagePreference languagePreference) => languagePreference switch
    {
        LanguagePreference.English => UserLanguages.English,
        LanguagePreference.Korean => UserLanguages.Korean,
        _ => DeviceInformationHelper.GetInterfaceLanguage(),
    };

    private static void ApplyLanguage(LanguagePreference languagePreference)
    {
        var languageTag = languagePreference switch
        {
            LanguagePreference.English => UserLanguages.English,
            LanguagePreference.Korean => UserLanguages.Korean,
            _ => string.Empty,
        };

        ApplicationLanguages.PrimaryLanguageOverride = languageTag;
        if (languageTag.Length == 0) return;

        var culture = new CultureInfo(languageTag);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static ElementTheme ToElementTheme(ThemePreference themePreference) => themePreference switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
