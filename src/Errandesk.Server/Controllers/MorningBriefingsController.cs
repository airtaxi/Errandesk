using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's morning briefings. Briefings are written by the server; their settings are part of the user profile.
/// </summary>
[ApiController]
[Route("api/briefings")]
[Authorize]
public sealed class MorningBriefingsController(MorningBriefingService briefingService) : ControllerBase
{
    /// <summary>The most recent briefing, or 204 when none was written yet.</summary>
    [HttpGet("latest")]
    [ProducesResponseType<MorningBriefingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<MorningBriefingResponse>> GetLatestAsync(CancellationToken cancellationToken) => await briefingService.GetLatestAsync(User.GetUserId(), cancellationToken) is { } briefing ? Ok(briefing) : NoContent();
}
