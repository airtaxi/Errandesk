using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's tasks and their steps.
/// </summary>
[ApiController]
[Route("api/tasks")]
[Authorize]
public sealed class TasksController(TaskService taskService) : ControllerBase
{
    /// <summary>Tasks of a view: <c>myday</c>, <c>important</c>, <c>planned</c> or a list ID; every task when no view is given.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TaskItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<TaskItemResponse>> QueryAsync([FromQuery] string? view, [FromQuery] bool includeCompleted, CancellationToken cancellationToken) => taskService.QueryAsync(User.GetUserId(), view, includeCompleted, cancellationToken);

    [HttpGet("{taskId:guid}")]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskItemResponse> GetAsync(Guid taskId, CancellationToken cancellationToken) => taskService.GetAsync(User.GetUserId(), taskId, cancellationToken);

    /// <summary>Creates a task, or returns the existing task when the client-supplied ID was already created by the same user.</summary>
    [HttpPost]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskItemResponse>> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await taskService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return result.IsCreated ? Created($"/api/tasks/{result.Resource.Id}", result.Resource) : Ok(result.Resource);
    }

    [HttpPatch("{taskId:guid}")]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status409Conflict)]
    public Task<TaskItemResponse> UpdateAsync(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken) => taskService.UpdateAsync(User.GetUserId(), taskId, request, cancellationToken);

    [HttpDelete("{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> DeleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await taskService.DeleteAsync(User.GetUserId(), taskId, cancellationToken);
        return NoContent();
    }

    /// <summary>Adds a step and returns the whole task.</summary>
    [HttpPost("{taskId:guid}/steps")]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskItemResponse> AddStepAsync(Guid taskId, CreateTaskStepRequest request, CancellationToken cancellationToken) => taskService.AddStepAsync(User.GetUserId(), taskId, request, cancellationToken);

    /// <summary>Changes a step and returns the whole task.</summary>
    [HttpPatch("{taskId:guid}/steps/{stepId:guid}")]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status409Conflict)]
    public Task<TaskItemResponse> UpdateStepAsync(Guid taskId, Guid stepId, UpdateTaskStepRequest request, CancellationToken cancellationToken) => taskService.UpdateStepAsync(User.GetUserId(), taskId, stepId, request, cancellationToken);

    /// <summary>Removes a step and returns the whole task.</summary>
    [HttpDelete("{taskId:guid}/steps/{stepId:guid}")]
    [ProducesResponseType<TaskItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskItemResponse> DeleteStepAsync(Guid taskId, Guid stepId, CancellationToken cancellationToken) => taskService.DeleteStepAsync(User.GetUserId(), taskId, stepId, cancellationToken);
}
