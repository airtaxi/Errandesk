using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's personal access tokens for the MCP endpoint and the API. Managing tokens requires a signed-in session; a personal access token cannot manage tokens.
/// </summary>
[ApiController]
[Route("api/users/me/tokens")]
[Authorize(Policy = ErrandeskAuthenticationSchemes.SignedInSessionPolicy)]
public sealed class PersonalAccessTokensController(PersonalAccessTokenService personalAccessTokenService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PersonalAccessTokenResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PersonalAccessTokenResponse>> ListAsync(CancellationToken cancellationToken) => personalAccessTokenService.ListAsync(User.GetUserId(), cancellationToken);

    /// <summary>Creates a token. The response is the only place its secret value appears.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedPersonalAccessTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<CreatedResult> CreateAsync(CreatePersonalAccessTokenRequest request, CancellationToken cancellationToken)
    {
        var created = await personalAccessTokenService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return Created($"/api/users/me/tokens/{created.PersonalAccessToken.Id}", created);
    }

    [HttpDelete("{tokenId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NoContentResult> RevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        await personalAccessTokenService.RevokeAsync(User.GetUserId(), tokenId, cancellationToken);
        return NoContent();
    }
}
