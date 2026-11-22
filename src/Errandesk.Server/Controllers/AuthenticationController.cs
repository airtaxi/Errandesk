using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Registration, sign-in, token refresh and sign-out.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthenticationController(AccountService accountService) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(AuthenticationRateLimiting.PolicyName)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<AuthenticationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken) => accountService.RegisterAsync(request, cancellationToken);

    [HttpPost("login")]
    [EnableRateLimiting(AuthenticationRateLimiting.PolicyName)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<AuthenticationResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken) => accountService.LoginAsync(request, cancellationToken);

    [HttpPost("refresh")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken) => accountService.RefreshAsync(request, cancellationToken);

    /// <summary>Ends the session of the given refresh token on the calling device.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<NoContentResult> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await accountService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }
}
