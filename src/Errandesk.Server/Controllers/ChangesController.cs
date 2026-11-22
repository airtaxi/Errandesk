using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The change feed of the signed-in user's lists, tasks and notes.
/// </summary>
[ApiController]
[Route("api/changes")]
[Authorize]
public sealed class ChangesController(ChangeFeedService changeFeedService) : ControllerBase
{
    /// <summary>Lists, tasks and notes changed after <paramref name="since"/>, with tombstones of deleted items. Recently changed items may repeat.</summary>
    [HttpGet]
    [ProducesResponseType<ChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<ChangesResponse> GetAsync([FromQuery] long since, CancellationToken cancellationToken) => changeFeedService.GetChangesAsync(User.GetUserId(), since, cancellationToken);
}
