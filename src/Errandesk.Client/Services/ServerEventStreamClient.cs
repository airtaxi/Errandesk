using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Client.Managers;
using Errandesk.Contracts;

namespace Errandesk.Client.Services;

/// <summary>
/// Opens the server-sent event streams of the Errandesk server: assistant replies and change notifications.
/// The HTTP client carries <see cref="AuthenticationHandler"/> and has no timeout, because the streams stay open.
/// </summary>
/// <remarks>
/// The open methods throw <see cref="ApiException"/> when the server rejects the request before the stream starts,
/// <see cref="HttpRequestException"/> when the server cannot be reached and <see cref="SessionExpiredException"/> when the session ended.
/// </remarks>
public sealed class ServerEventStreamClient(HttpClient httpClient, SessionManager sessionManager)
{
    private const string EventStreamMediaType = "text/event-stream";

    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    /// <summary>
    /// Sends a message to the assistant and opens the stream of its reply.
    /// </summary>
    public Task<HttpResponseMessage> OpenMessageStreamAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken) => OpenAsync(HttpMethod.Post, $"api/conversations/{conversationId}/messages", JsonContent.Create(request, JsonContext.SendMessageRequest), cancellationToken);

    /// <summary>
    /// Decides every pending approval request of a conversation and opens the stream of the resumed reply.
    /// </summary>
    public Task<HttpResponseMessage> OpenApprovalStreamAsync(Guid conversationId, ApprovalDecisionsRequest request, CancellationToken cancellationToken) => OpenAsync(HttpMethod.Post, $"api/conversations/{conversationId}/approvals", JsonContent.Create(request, JsonContext.ApprovalDecisionsRequest), cancellationToken);

    /// <summary>
    /// Opens the stream that reports changes to the user's data.
    /// </summary>
    public Task<HttpResponseMessage> OpenChangeEventStreamAsync(CancellationToken cancellationToken) => OpenAsync(HttpMethod.Get, "api/events", null, cancellationToken);

    /// <summary>
    /// Reads the events of an open stream until the server ends it. Canceling <paramref name="cancellationToken"/> closes the response.
    /// </summary>
    public static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(response.Dispose);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancellationToken)) yield return item;
    }

    /// <summary>
    /// Reads the JSON data of an event.
    /// </summary>
    /// <returns>The data, or <see langword="null"/> when it is not the expected JSON.</returns>
    public static TData? ParseData<TData>(SseItem<string> item, JsonTypeInfo<TData> typeInfo) where TData : class
    {
        try { return JsonSerializer.Deserialize(item.Data, typeInfo); }
        catch (JsonException) { return null; }
    }

    private async Task<HttpResponseMessage> OpenAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(sessionManager.ServerAddress, path)) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventStreamMediaType));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await ApiResponseReader.EnsureSuccessAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
