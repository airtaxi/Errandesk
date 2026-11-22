using System.Globalization;
using System.Threading.RateLimiting;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace Errandesk.Server.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    private static readonly TimeSpan s_clockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers JWT bearer and personal access token authentication, authorization, password hashing, the access token issuer, and the sign-in and list sharing rate limiters.
    /// </summary>
    public static IServiceCollection AddErrandeskAuthentication(this IServiceCollection services, ServerSettings settings)
    {
        services.AddSingleton<IPasswordHasher<UserDocument>, PasswordHasher<UserDocument>>();
        services.AddSingleton<AccessTokenIssuer>();
        services.AddSingleton<PersonalAccessTokenService>();

        services.AddAuthentication(ErrandeskAuthenticationSchemes.Default)
            .AddPolicyScheme(ErrandeskAuthenticationSchemes.Default, ErrandeskAuthenticationSchemes.Default, options => options.ForwardDefaultSelector = SelectAuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenAuthenticationHandler>(ErrandeskAuthenticationSchemes.PersonalAccessToken, null);
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<TimeProvider>((options, timeProvider) =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = AccessTokenIssuer.Issuer,
                ValidAudience = AccessTokenIssuer.Audience,
                IssuerSigningKey = AccessTokenIssuer.CreateSigningKey(settings.JwtSigningKey),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = ErrandeskClaimTypes.Name,
                RoleClaimType = ErrandeskClaimTypes.Role,
                ClockSkew = s_clockSkew,
                LifetimeValidator = (notBefore, expires, _, _) => IsWithinLifetime(notBefore, expires, timeProvider.GetUtcNow().UtcDateTime),
            };
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ErrandeskAuthenticationSchemes.PersonalAccessTokenPolicy, policy => policy.AddAuthenticationSchemes(ErrandeskAuthenticationSchemes.PersonalAccessToken).RequireAuthenticatedUser());
            options.AddPolicy(ErrandeskAuthenticationSchemes.SignedInSessionPolicy, policy => policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRateLimitProblemAsync;
            options.AddPolicy(AuthenticationRateLimiting.PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = AuthenticationRateLimiting.PermitLimit, Window = AuthenticationRateLimiting.Window, QueueLimit = 0 }));
            options.AddPolicy(ListSharingRateLimiting.PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(httpContext.User.FindFirst(ErrandeskClaimTypes.Subject)?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = ListSharingRateLimiting.PermitLimit, Window = ListSharingRateLimiting.Window, QueueLimit = 0 }));
        });
        return services;
    }

    private static string SelectAuthenticationScheme(HttpContext context) => PersonalAccessTokens.HasPrefix(PersonalAccessTokens.ReadBearerToken(context.Request)) ? ErrandeskAuthenticationSchemes.PersonalAccessToken : JwtBearerDefaults.AuthenticationScheme;

    private static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, DateTime utcNow) => expires is { } expiry && expiry.ToUniversalTime() > utcNow - s_clockSkew && (notBefore is not { } start || start.ToUniversalTime() <= utcNow + s_clockSkew);

    private static async ValueTask WriteRateLimitProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)) context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var isListSharing = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == ListSharingRateLimiting.PolicyName;
        var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = isListSharing ? "Too many attempts to share lists. Try again later." : "Too many sign-in or registration attempts. Try again later.",
            },
        });
    }
}
