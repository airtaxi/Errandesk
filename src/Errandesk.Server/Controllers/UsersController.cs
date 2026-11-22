using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's profile and password.
/// </summary>
[ApiController]
[Route("api/users/me")]
[Authorize]
public sealed class UsersController(UserService userService, AccountService accountService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public Task<UserResponse> GetAsync(CancellationToken cancellationToken) => userService.GetProfileAsync(User.GetUserId(), cancellationToken);

    [HttpPatch]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<UserResponse> UpdateAsync(UpdateUserRequest request, CancellationToken cancellationToken) => userService.UpdateProfileAsync(User.GetUserId(), request, cancellationToken);

    /// <summary>Changes the password and signs out every other session of the user.</summary>
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<NoContentResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await accountService.ChangePasswordAsync(User.GetUserId(), User.GetSessionId(), request, cancellationToken);
        return NoContent();
    }
}
