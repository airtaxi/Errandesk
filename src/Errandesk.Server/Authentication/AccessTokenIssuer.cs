using System.Text;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Errandesk.Server.Authentication;

/// <summary>
/// Issues HMAC-SHA256 signed JWT access tokens for a user session.
/// </summary>
public sealed class AccessTokenIssuer(ServerSettings settings, TimeProvider timeProvider)
{
    public const string Issuer = "errandesk";
    public const string Audience = "errandesk";

    private readonly SigningCredentials _signingCredentials = new(CreateSigningKey(settings.JwtSigningKey), SecurityAlgorithms.HmacSha256);
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public static SymmetricSecurityKey CreateSigningKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public IssuedAccessToken Issue(UserDocument user, Guid sessionId)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt + settings.AccessTokenLifetime;
        var claims = new Dictionary<string, object>
        {
            [ErrandeskClaimTypes.Subject] = user.Id.ToString(),
            [ErrandeskClaimTypes.SessionId] = sessionId.ToString(),
            [ErrandeskClaimTypes.Name] = user.UserName,
        };
        if (user.IsAdministrator) claims[ErrandeskClaimTypes.Role] = ErrandeskRoles.Administrator;

        var token = _tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        });
        return new IssuedAccessToken(token, expiresAt);
    }
}
