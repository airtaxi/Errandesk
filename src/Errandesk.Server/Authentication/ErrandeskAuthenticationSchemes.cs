namespace Errandesk.Server.Authentication;

/// <summary>
/// Names of the authentication schemes and authorization policies.
/// </summary>
public static class ErrandeskAuthenticationSchemes
{
    /// <summary>The default scheme: forwards bearer tokens with the personal access token prefix to <see cref="PersonalAccessToken"/>, and every other request to JWT bearer authentication.</summary>
    public const string Default = "Errandesk";

    public const string PersonalAccessToken = "PersonalAccessToken";

    /// <summary>Authorization policy that accepts only personal access tokens.</summary>
    public const string PersonalAccessTokenPolicy = "PersonalAccessTokenOnly";

    /// <summary>Authorization policy that accepts only access tokens of a signed-in session.</summary>
    public const string SignedInSessionPolicy = "SignedInSessionOnly";
}
