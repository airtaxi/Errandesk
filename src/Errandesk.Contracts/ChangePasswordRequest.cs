namespace Errandesk.Contracts;

/// <summary>
/// Changes the signed-in user's password. Every other session of the user is signed out.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
