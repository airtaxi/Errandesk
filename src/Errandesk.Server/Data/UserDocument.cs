namespace Errandesk.Server.Data;

/// <summary>
/// An account. <see cref="DocumentBase.OwnerUserId"/> equals <see cref="DocumentBase.Id"/>.
/// </summary>
public sealed class UserDocument : DocumentBase
{
    /// <summary>User name as entered at registration.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Upper-case invariant form of <see cref="UserName"/>, unique across users.</summary>
    public string NormalizedUserName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public bool IsAdministrator { get; set; }

    /// <summary>Marks the first registered user. A unique partial index allows only one such user.</summary>
    public bool IsFirstUser { get; set; }

    /// <summary>IANA time zone ID.</summary>
    public string TimeZone { get; set; } = "";

    /// <summary><c>en</c> or <c>ko</c>.</summary>
    public string Language { get; set; } = "";

    /// <summary>Morning briefing settings, or <see langword="null"/> while the user never changed them.</summary>
    public MorningBriefingSettingsDocument? MorningBriefing { get; set; }
}
