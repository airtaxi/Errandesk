namespace Errandesk.Contracts;

/// <summary>
/// An account on the server.
/// </summary>
/// <param name="Id">User ID.</param>
/// <param name="UserName">Unique user name as entered at registration.</param>
/// <param name="DisplayName">Name shown in the apps.</param>
/// <param name="IsAdministrator">Whether the user can manage invites and see all users.</param>
/// <param name="TimeZone">IANA time zone that defines the user's "today".</param>
/// <param name="Language">Language of the user, <c>en</c> or <c>ko</c>.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="UpdatedAt">When the account was last changed.</param>
/// <param name="ChangeVersion">Global change version of the last change.</param>
/// <param name="MorningBriefing">When the assistant writes the user's morning briefing.</param>
public sealed record UserResponse(Guid Id, string UserName, string DisplayName, bool IsAdministrator, string TimeZone, string Language, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long ChangeVersion, MorningBriefingSettings? MorningBriefing = null);
