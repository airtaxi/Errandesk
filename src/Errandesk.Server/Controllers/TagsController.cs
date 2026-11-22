using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Tags of the signed-in user's notes.
/// </summary>
[ApiController]
[Route("api/tags")]
[Authorize]
public sealed class TagsController(NoteService noteService) : ControllerBase
{
    /// <summary>Every tag with the number of notes that carry it, ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TagResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TagResponse>> ListAsync(CancellationToken cancellationToken) => noteService.ListTagsAsync(User.GetUserId(), cancellationToken);
}
