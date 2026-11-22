using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's client devices, which receive reminder push notifications on Android and iOS.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController(DeviceService deviceService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeviceResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken) => deviceService.ListAsync(User.GetUserId(), cancellationToken);

    /// <summary>Registers the device, or updates it when its ID is already registered.</summary>
    [HttpPost]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DeviceResponse>> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        var result = await deviceService.RegisterAsync(User.GetUserId(), request, cancellationToken);
        return result.IsCreated ? Created($"/api/devices/{result.Resource.Id}", result.Resource) : Ok(result.Resource);
    }

    [HttpDelete("{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> DeleteAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await deviceService.DeleteAsync(User.GetUserId(), deviceId, cancellationToken);
        return NoContent();
    }
}
