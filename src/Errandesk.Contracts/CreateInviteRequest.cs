namespace Errandesk.Contracts;

/// <summary>
/// Creates a single-use invite code.
/// </summary>
/// <param name="ExpiresInDays">Days until the code expires, 1 to 365. Defaults to 7.</param>
public sealed record CreateInviteRequest(int? ExpiresInDays = null);
