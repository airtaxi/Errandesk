using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's notes and their links to tasks.
/// </summary>
[ApiController]
[Route("api/notes")]
[Authorize]
public sealed class NotesController(NoteService noteService) : ControllerBase
{
    /// <summary>Notes matching every given filter: text search with <c>query</c>, a <c>tag</c>, and a <c>linkedTaskId</c>. Pinned notes come first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NoteResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<NoteResponse>> QueryAsync([FromQuery] string? query, [FromQuery] string? tag, [FromQuery] Guid? linkedTaskId, CancellationToken cancellationToken) => noteService.QueryAsync(User.GetUserId(), query, tag, linkedTaskId, cancellationToken);

    [HttpGet("{noteId:guid}")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NoteResponse> GetAsync(Guid noteId, CancellationToken cancellationToken) => noteService.GetAsync(User.GetUserId(), noteId, cancellationToken);

    /// <summary>Creates a note, or returns the existing note when the client-supplied ID was already created by the same user.</summary>
    [HttpPost]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NoteResponse>> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken)
    {
        var result = await noteService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return result.IsCreated ? Created($"/api/notes/{result.Resource.Id}", result.Resource) : Ok(result.Resource);
    }

    [HttpPatch("{noteId:guid}")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status409Conflict)]
    public Task<NoteResponse> UpdateAsync(Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken) => noteService.UpdateAsync(User.GetUserId(), noteId, request, cancellationToken);

    [HttpDelete("{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> DeleteAsync(Guid noteId, CancellationToken cancellationToken)
    {
        await noteService.DeleteAsync(User.GetUserId(), noteId, cancellationToken);
        return NoContent();
    }

    /// <summary>Links the note to a task and returns the note. Linking an already linked task changes nothing.</summary>
    [HttpPut("{noteId:guid}/linked-tasks/{taskId:guid}")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NoteResponse> LinkTaskAsync(Guid noteId, Guid taskId, CancellationToken cancellationToken) => noteService.LinkTaskAsync(User.GetUserId(), noteId, taskId, cancellationToken);

    /// <summary>Removes the link between the note and a task and returns the note. Unlinking a task that is not linked changes nothing.</summary>
    [HttpDelete("{noteId:guid}/linked-tasks/{taskId:guid}")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NoteResponse> UnlinkTaskAsync(Guid noteId, Guid taskId, CancellationToken cancellationToken) => noteService.UnlinkTaskAsync(User.GetUserId(), noteId, taskId, cancellationToken);
}
