namespace Errandesk.Contracts;

/// <summary>
/// Carries a refresh token, either to exchange it for new tokens or to end its session.
/// </summary>
public sealed record RefreshTokenRequest(string RefreshToken);
