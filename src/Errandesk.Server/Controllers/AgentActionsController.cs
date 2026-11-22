using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Changes the assistant made for the signed-in user.
/// </summary>
[ApiController]
[Route("api/agent-actions")]
[Authorize]
public sealed class AgentActionsController(AgentActionService agentActionService) : ControllerBase
{
    [HttpGet("{actionId:guid}")]
    [ProducesResponseType<AgentActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AgentActionResponse> GetAsync(Guid actionId, CancellationToken cancellationToken) => agentActionService.GetAsync(User.GetUserId(), actionId, cancellationToken);

    /// <summary>Restores every affected item to its state before the action. Answers 409 when the action was already undone or an affected item changed since.</summary>
    [HttpPost("{actionId:guid}/undo")]
    [ProducesResponseType<AgentActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AgentActionResponse> UndoAsync(Guid actionId, CancellationToken cancellationToken) => agentActionService.UndoAsync(User.GetUserId(), actionId, cancellationToken);
}
