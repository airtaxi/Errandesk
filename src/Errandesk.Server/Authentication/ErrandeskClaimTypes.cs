namespace Errandesk.Server.Authentication;

/// <summary>
/// Claim names carried by access tokens.
/// </summary>
public static class ErrandeskClaimTypes
{
    /// <summary>The user ID.</summary>
    public const string Subject = "sub";

    /// <summary>The refresh token session the access token was issued for.</summary>
    public const string SessionId = "sid";

    public const string Name = "name";

    public const string Role = "role";

    /// <summary>The personal access token that authenticated the request.</summary>
    public const string PersonalAccessTokenId = "pat";
}
