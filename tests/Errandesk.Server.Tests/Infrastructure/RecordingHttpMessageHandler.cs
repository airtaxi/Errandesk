using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Answers HTTP requests with a callback instead of the network and records every request with its body.
/// </summary>
public sealed class RecordingHttpMessageHandler(Func<RecordedHttpRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedHttpRequest> _requests = new();

    public IReadOnlyList<RecordedHttpRequest> Requests =>
    [.. _requests];

    public static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recordedRequest = new RecordedHttpRequest(request.Method, request.RequestUri!, request.Version, request.VersionPolicy, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase), request.Content?.Headers.ContentType?.MediaType, body);
        _requests.Enqueue(recordedRequest);
        var response = respond(recordedRequest);
        response.RequestMessage = request;
        return response;
    }
}
