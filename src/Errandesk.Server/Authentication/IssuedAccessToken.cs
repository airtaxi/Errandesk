namespace Errandesk.Server.Authentication;

/// <summary>
/// A signed access token and its expiry.
/// </summary>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
