namespace Errandesk.Contracts;

/// <summary>
/// Tokens of a signed-in session and the signed-in user.
/// </summary>
/// <param name="AccessToken">Short-lived bearer token for API requests.</param>
/// <param name="AccessTokenExpiresAt">When the access token expires.</param>
/// <param name="RefreshToken">Single-use token that obtains the next token pair. Every refresh returns a new one.</param>
/// <param name="RefreshTokenExpiresAt">When the refresh token expires.</param>
/// <param name="User">The signed-in user.</param>
public sealed record AuthenticationResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, UserResponse User);
