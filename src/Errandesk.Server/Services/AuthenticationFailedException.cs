namespace Errandesk.Server.Services;

/// <summary>
/// Credentials or a refresh token were rejected.
/// </summary>
public sealed class AuthenticationFailedException(string detail) : DomainException("Authentication failed.", detail)
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;
}
