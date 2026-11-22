using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A personal access token as listed in the settings.
/// </summary>
public sealed class PersonalAccessTokenModel(PersonalAccessTokenResponse response)
{
    public Guid Id { get; } = response.Id;

    public string Name { get; } = response.Name;

    /// <summary>The first characters of the token, followed by an ellipsis.</summary>
    public string DisplayPrefixText { get; } = $"{response.DisplayPrefix}…";

    public string UsageText { get; } = response.LastUsedAt is { } lastUsedAt ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AccessTokenUsedFormat"), DateDisplayHelper.FormatShortDate(response.CreatedAt), DateDisplayHelper.FormatShortDate(lastUsedAt)) : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AccessTokenUnusedFormat"), DateDisplayHelper.FormatShortDate(response.CreatedAt));

    public override string ToString() => Name;
}
