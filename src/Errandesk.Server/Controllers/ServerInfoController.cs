using Errandesk.Contracts;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// Describes the server to clients before they sign in.
/// </summary>
[ApiController]
[Route("api/server-info")]
[AllowAnonymous]
public sealed class ServerInfoController(ServerInfoService serverInfoService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ServerInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken) => serverInfoService.GetServerInfoAsync(cancellationToken);
}
