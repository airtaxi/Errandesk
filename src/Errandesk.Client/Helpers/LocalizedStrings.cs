using Microsoft.Windows.ApplicationModel.Resources;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Reads user-facing strings from the localized string resources.
/// </summary>
public static class LocalizedStrings
{
    private static readonly ResourceLoader s_resourceLoader = new();

    public static string Get(string resourceName) => s_resourceLoader.GetString(resourceName);
}
