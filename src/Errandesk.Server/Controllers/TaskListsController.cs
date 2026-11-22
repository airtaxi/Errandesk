using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The task lists the signed-in user owns or is a member of, and their members.
/// </summary>
[ApiController]
[Route("api/lists")]
[Authorize]
public sealed class TaskListsController(TaskListService taskListService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TaskListResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TaskListResponse>> ListAsync(CancellationToken cancellationToken) => taskListService.ListAsync(User.GetUserId(), cancellationToken);

    [HttpGet("{listId:guid}")]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskListResponse> GetAsync(Guid listId, CancellationToken cancellationToken) => taskListService.GetAsync(User.GetUserId(), listId, cancellationToken);

    /// <summary>Creates a list, or returns the existing list when the client-supplied ID was already created by the same user.</summary>
    [HttpPost]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskListResponse>> CreateAsync(CreateTaskListRequest request, CancellationToken cancellationToken)
    {
        var result = await taskListService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return result.IsCreated ? Created($"/api/lists/{result.Resource.Id}", result.Resource) : Ok(result.Resource);
    }

    /// <summary>Renames or moves a list. Members get 403.</summary>
    [HttpPatch("{listId:guid}")]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status409Conflict)]
    public Task<TaskListResponse> UpdateAsync(Guid listId, UpdateTaskListRequest request, CancellationToken cancellationToken) => taskListService.UpdateAsync(User.GetUserId(), listId, request, cancellationToken);

    /// <summary>Deletes a list and its tasks. Members get 403 and leave the list instead.</summary>
    [HttpDelete("{listId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> DeleteAsync(Guid listId, CancellationToken cancellationToken)
    {
        await taskListService.DeleteAsync(User.GetUserId(), listId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Shares the list with the user who has exactly this user name, ignoring case. Only the owner may share. An unknown name answers 404 like any other name that matches nobody.
    /// Rate limited per user.
    /// </summary>
    [HttpPost("{listId:guid}/members")]
    [EnableRateLimiting(ListSharingRateLimiting.PolicyName)]
    [ProducesResponseType<TaskListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<TaskListResponse> AddMemberAsync(Guid listId, AddTaskListMemberRequest request, CancellationToken cancellationToken) => taskListService.AddMemberAsync(User.GetUserId(), listId, request, cancellationToken);

    /// <summary>Removes a member. The owner may remove any member; a member may remove only themselves, which leaves the list.</summary>
    [HttpDelete("{listId:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> RemoveMemberAsync(Guid listId, Guid userId, CancellationToken cancellationToken)
    {
        await taskListService.RemoveMemberAsync(User.GetUserId(), listId, userId, cancellationToken);
        return NoContent();
    }
}
