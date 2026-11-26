using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// The signed-in user's assistant conversations. Every endpoint answers 503 when the server has no AI model configured.
/// </summary>
[ApiController]
[Route("api/conversations")]
[Authorize]
public sealed class ConversationsController(ConversationService conversationService, ConversationAttachmentService attachmentService, AssistantConversationRunner runner) : ControllerBase
{
    /// <summary>Conversations, most recently changed first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<ConversationResponse>> ListAsync(CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        return conversationService.ListAsync(User.GetUserId(), cancellationToken);
    }

    /// <summary>Creates a conversation, or returns the existing conversation when the client-supplied ID was already created by the same user.</summary>
    [HttpPost]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConversationResponse>> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        var result = await conversationService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return result.IsCreated ? Created($"/api/conversations/{result.Resource.Id}", result.Resource) : Ok(result.Resource);
    }

    [HttpGet("{conversationId:guid}")]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<ConversationResponse> GetAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        return conversationService.GetAsync(User.GetUserId(), conversationId, cancellationToken);
    }

    [HttpDelete("{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<NoContentResult> DeleteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        await conversationService.DeleteAsync(User.GetUserId(), conversationId, cancellationToken);
        return NoContent();
    }

    /// <summary>The conversation history: user messages and one assistant message per reply, with tool activity and approval requests.</summary>
    [HttpGet("{conversationId:guid}/messages")]
    [ProducesResponseType<IReadOnlyList<ConversationMessageResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<ConversationMessageResponse>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        return conversationService.GetMessagesAsync(User.GetUserId(), conversationId, cancellationToken);
    }

    /// <summary>
    /// Stores an image for the next message of the conversation. The request body is the image file: JPEG, PNG, GIF or WebP,
    /// at most <see cref="ConversationAttachmentLimits.MaximumLength"/> bytes. Images that are not sent within a day are removed.
    /// </summary>
    [HttpPost("{conversationId:guid}/attachments")]
    [ProducesResponseType<ConversationAttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConversationAttachmentResponse>> CreateAttachmentAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        var attachment = await attachmentService.CreateAsync(User.GetUserId(), conversationId, Request.Body, cancellationToken);
        return Created($"/api/conversations/{conversationId}/attachments/{attachment.Id}", attachment);
    }

    /// <summary>The image file of an attachment. Its content never changes, so clients may cache it.</summary>
    [HttpGet("{conversationId:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ImageMediaTypes.Jpeg, ImageMediaTypes.Png, ImageMediaTypes.Gif, ImageMediaTypes.Webp)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<FileContentResult> GetAttachmentAsync(Guid conversationId, Guid attachmentId, CancellationToken cancellationToken)
    {
        runner.EnsureAvailable();
        var attachment = await attachmentService.GetAsync(User.GetUserId(), conversationId, attachmentId, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(attachment.Data, attachment.MediaType);
    }

    /// <summary>Sends a message with optional images and streams the reply as server-sent events. Pending approval requests are denied.</summary>
    [HttpPost("{conversationId:guid}/messages")]
    [IdempotencyExempt]
    [Produces(ServerSentEventWriter.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EmptyResult> SendMessageAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var run = await runner.StartMessageRunAsync(User.GetUserId(), conversationId, request, cancellationToken);
        await StreamAsync(run, cancellationToken);
        return new EmptyResult();
    }

    /// <summary>Decides every pending approval request and streams the resumed reply as server-sent events.</summary>
    [HttpPost("{conversationId:guid}/approvals")]
    [IdempotencyExempt]
    [Produces(ServerSentEventWriter.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EmptyResult> DecideApprovalsAsync(Guid conversationId, ApprovalDecisionsRequest request, CancellationToken cancellationToken)
    {
        var run = await runner.StartApprovalRunAsync(User.GetUserId(), conversationId, request, cancellationToken);
        await StreamAsync(run, cancellationToken);
        return new EmptyResult();
    }

    private async Task StreamAsync(AssistantRun run, CancellationToken cancellationToken)
    {
        ServerSentEventWriter writer;
        try { writer = await ServerSentEventWriter.StartAsync(Response, cancellationToken); }
        catch
        {
            await conversationService.ReleaseRunAsync(run.ConversationId, run.RunId);
            throw;
        }
        await runner.ExecuteAsync(run, writer, cancellationToken);
    }
}
