using System.Security.Claims;

namespace Errandesk.Server.Authentication;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's ID from the subject claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirstValue(ErrandeskClaimTypes.Subject), out var userId) ? userId : throw new InvalidOperationException("The principal has no user ID claim.");

    /// <summary>The refresh token session of the access token, or <see langword="null"/> when the token has none.</summary>
    public static Guid? GetSessionId(this ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirstValue(ErrandeskClaimTypes.SessionId), out var sessionId) ? sessionId : null;
}
