namespace Errandesk.Contracts;

/// <summary>
/// Creates an account and signs it in on the calling device.
/// </summary>
/// <param name="UserName">Unique user name, compared case-insensitively. 3 to 32 letters, digits, dots, hyphens or underscores.</param>
/// <param name="Password">Password of at least 8 characters.</param>
/// <param name="TimeZone">IANA time zone of the user, such as <c>Asia/Seoul</c>.</param>
/// <param name="Language">Language of the user, <c>en</c> or <c>ko</c>.</param>
/// <param name="DisplayName">Name shown in the apps. Defaults to the user name.</param>
/// <param name="InviteCode">Invite code, required when the server is in invite mode and users already exist.</param>
/// <param name="DeviceName">Name of the signing-in device.</param>
public sealed record RegisterRequest(string UserName, string Password, string TimeZone, string Language, string? DisplayName = null, string? InviteCode = null, string? DeviceName = null);
