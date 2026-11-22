using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Errandesk.Server.Authentication;

/// <summary>
/// Authenticates <c>Authorization: Bearer edk_...</c> requests with a personal access token of an existing user.
/// </summary>
public sealed class PersonalAccessTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, PersonalAccessTokenService personalAccessTokenService) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = PersonalAccessTokens.ReadBearerToken(Request);
        if (!PersonalAccessTokens.HasPrefix(token)) return AuthenticateResult.NoResult();

        var authentication = await personalAccessTokenService.AuthenticateAsync(token!, Context.RequestAborted);
        if (authentication is null) return AuthenticateResult.Fail("The personal access token is not valid.");

        List<Claim> claims =
        [
            new(ErrandeskClaimTypes.Subject, authentication.User.Id.ToString()),
            new(ErrandeskClaimTypes.Name, authentication.User.UserName),
            new(ErrandeskClaimTypes.PersonalAccessTokenId, authentication.TokenId.ToString("D", CultureInfo.InvariantCulture)),
        ];
        if (authentication.User.IsAdministrator) claims.Add(new Claim(ErrandeskClaimTypes.Role, ErrandeskRoles.Administrator));

        var identity = new ClaimsIdentity(claims, Scheme.Name, ErrandeskClaimTypes.Name, ErrandeskClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
