using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// An invite code as listed in the settings.
/// </summary>
public sealed class InviteModel(InviteResponse response, DateTimeOffset now)
{
    public Guid Id { get; } = response.Id;

    public string Code { get; } = response.Code;

    /// <summary>
    /// Whether the code can still be used to register.
    /// </summary>
    public bool IsUsable { get; } = response.UsedAt is null && response.ExpiresAt > now;

    public override string ToString() => Code;

    public string StatusText { get; } = response switch
    {
        { UsedAt: { } usedAt } => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("InviteUsedFormat"), DateDisplayHelper.FormatShortDate(usedAt)),
        _ when response.ExpiresAt <= now => LocalizedStrings.Get("InviteExpiredText"),
        _ => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("InviteExpiresFormat"), DateDisplayHelper.FormatShortDate(response.ExpiresAt)),
    };
}
