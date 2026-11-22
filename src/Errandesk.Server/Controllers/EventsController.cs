using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandesk.Server.Controllers;

/// <summary>
/// A server-sent event stream that tells the signed-in user's clients when their data changed.
/// </summary>
[ApiController]
[Route("api/events")]
[Authorize]
public sealed class EventsController(ChangeNotifier changeNotifier, EventStreamOptions options) : ControllerBase
{
    /// <summary>Emits <c>changed</c> with the new change version whenever lists, tasks or notes of the user change, and a keep-alive comment while idle.</summary>
    [HttpGet]
    [Produces(ServerSentEventWriter.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<EmptyResult> StreamAsync(CancellationToken cancellationToken)
    {
        using var subscription = changeNotifier.Subscribe(User.GetUserId());
        try
        {
            var writer = await ServerSentEventWriter.StartAsync(Response, cancellationToken);
            await writer.WriteCommentAsync("connected", cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                using var keepAliveSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                keepAliveSource.CancelAfter(options.KeepAliveInterval);
                try
                {
                    var changeVersion = await subscription.Reader.ReadAsync(keepAliveSource.Token);
                    await writer.WriteEventAsync(ServerEventNames.Changed, new ChangedEventData(changeVersion), ErrandeskJsonSerializerContext.Default.ChangedEventData, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { await writer.WriteCommentAsync("keep-alive", cancellationToken); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new EmptyResult();
    }
}
