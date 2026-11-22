namespace Errandesk.Server.Authentication;

/// <summary>
/// Rate limit applied to sign-in and registration, per client IP address.
/// </summary>
public static class AuthenticationRateLimiting
{
    public const string PolicyName = "authentication";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
