using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Administrator endpoints: users and invite codes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = ErrandeskRoles.Administrator)]
public sealed class AdministrationController(UserService userService, InviteService inviteService) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UserResponse>> ListUsersAsync(CancellationToken cancellationToken) => userService.ListAsync(cancellationToken);

    [HttpGet("invites")]
    [ProducesResponseType<IReadOnlyList<InviteResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InviteResponse>> ListInvitesAsync(CancellationToken cancellationToken) => inviteService.ListAsync(cancellationToken);

    [HttpPost("invites")]
    [ProducesResponseType<InviteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<CreatedResult> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken)
    {
        var invite = await inviteService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return Created($"/api/admin/invites/{invite.Id}", invite);
    }

    [HttpDelete("invites/{inviteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> DeleteInviteAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        await inviteService.DeleteAsync(inviteId, cancellationToken);
        return NoContent();
    }
}
