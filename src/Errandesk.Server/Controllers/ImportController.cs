using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Imports the signed-in user's data from other services.
/// </summary>
[ApiController]
[Route("api/import")]
[Authorize]
public sealed class ImportController(MicrosoftTodoImportService microsoftTodoImportService) : ControllerBase
{
    /// <summary>
    /// Starts a Microsoft To Do import. The response carries the code the user enters at the verification address; the server imports once the user has signed in.
    /// </summary>
    [HttpPost("microsoft-todo")]
    [ProducesResponseType<ImportJobResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ImportJobResponse>> StartMicrosoftTodoImportAsync(CancellationToken cancellationToken)
    {
        var job = await microsoftTodoImportService.StartAsync(User.GetUserId(), cancellationToken);
        return Created($"/api/import/jobs/{job.Id}", job);
    }

    /// <summary>The status and counts of an import job.</summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType<ImportJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ImportJobResponse> GetJobAsync(Guid jobId, CancellationToken cancellationToken) => microsoftTodoImportService.GetAsync(User.GetUserId(), jobId, cancellationToken);
}
