using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Features;

namespace Errandesk.Server.Services;

/// <summary>
/// Writes a <c>text/event-stream</c> response: named events with JSON data and comment lines, each flushed immediately.
/// </summary>
public sealed class ServerSentEventWriter
{
    public const string ContentType = "text/event-stream";

    private readonly HttpResponse _response;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private ServerSentEventWriter(HttpResponse response) => _response = response;

    /// <summary>Sends the response headers of an event stream and returns a writer for its events.</summary>
    public static async Task<ServerSentEventWriter> StartAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentType;
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.StartAsync(cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        return new ServerSentEventWriter(response);
    }

    public Task WriteEventAsync<TData>(string eventName, TData data, JsonTypeInfo<TData> typeInfo, CancellationToken cancellationToken) => WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(data, typeInfo)}\n\n", cancellationToken);

    /// <summary>Writes a comment line, which clients ignore; used to keep idle connections open.</summary>
    public Task WriteCommentAsync(string comment, CancellationToken cancellationToken) => WriteAsync($": {comment}\n\n", cancellationToken);

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            await _response.Body.FlushAsync(cancellationToken);
        }
        finally { _writeLock.Release(); }
    }
}
