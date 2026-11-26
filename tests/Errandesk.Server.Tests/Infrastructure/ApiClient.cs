using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Contracts;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Sends API requests with an optional bearer token, serializing and deserializing through the contracts JSON context like the client app.
/// </summary>
public sealed class ApiClient(HttpClient httpClient, string? accessToken = null)
{
    public const string ProblemMediaType = "application/problem+json";
    public const string EventStreamMediaType = "text/event-stream";

    public string? AccessToken { get; set; } = accessToken;

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (AccessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (idempotencyKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = new StringContent(SerializeJson(body), Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends <paramref name="body"/> as the raw request body with the given content type.</summary>
    public async Task<HttpResponseMessage> SendBytesAsync(HttpMethod method, string path, byte[] body, string contentType)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(body) };
        if (AccessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends the request, asserts the status code and deserializes the body.</summary>
    public async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body = null, HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        var content = await SendExpectingAsync(method, path, body, expectedStatusCode);
        return DeserializeJson<TResponse>(content);
    }

    /// <summary>Sends the request, asserts the status code and returns the body text.</summary>
    public async Task<string> SendExpectingAsync(HttpMethod method, string path, object? body, HttpStatusCode expectedStatusCode)
    {
        using var response = await SendAsync(method, path, body);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expectedStatusCode, $"{method} {path}: expected {(int)expectedStatusCode} but got {(int)response.StatusCode}. Body: {content}");
        return content;
    }

    /// <summary>Sends the request and asserts an RFC 7807 problem response with the given status code.</summary>
    public async Task<JsonElement> SendExpectingProblemAsync(HttpMethod method, string path, object? body, HttpStatusCode expectedStatusCode)
    {
        using var response = await SendAsync(method, path, body);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expectedStatusCode, $"{method} {path}: expected {(int)expectedStatusCode} but got {(int)response.StatusCode}. Body: {content}");
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(content);
        Assert.Equal((int)expectedStatusCode, document.RootElement.GetProperty("status").GetInt32());
        return document.RootElement.Clone();
    }

    /// <summary>Posts the request, asserts a complete event stream response and returns its events in order.</summary>
    public async Task<IReadOnlyList<ReceivedServerEvent>> PostForEventsAsync(string path, object body)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body);
        if (response.StatusCode != HttpStatusCode.OK) Assert.Fail($"POST {path}: expected 200 but got {(int)response.StatusCode}. Body: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(EventStreamMediaType, response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        List<ReceivedServerEvent> events = [];
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(TestContext.Current.CancellationToken)) events.Add(new ReceivedServerEvent(item.EventType, item.Data));
        return events;
    }

    /// <summary>Opens a long-running GET request and returns the response as soon as its headers arrive.</summary>
    public async Task<HttpResponseMessage> OpenStreamAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (AccessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public Task<TResponse> GetAsync<TResponse>(string path) => SendAsync<TResponse>(HttpMethod.Get, path);

    public Task<TResponse> PostAsync<TResponse>(string path, object body, HttpStatusCode expectedStatusCode = HttpStatusCode.OK) => SendAsync<TResponse>(HttpMethod.Post, path, body, expectedStatusCode);

    public Task<TResponse> PatchAsync<TResponse>(string path, object body) => SendAsync<TResponse>(HttpMethod.Patch, path, body);

    public Task DeleteAsync(string path) => SendExpectingAsync(HttpMethod.Delete, path, null, HttpStatusCode.NoContent);

    public static string SerializeJson(object value) => JsonSerializer.Serialize(value, value.GetType(), ErrandeskJsonSerializerContext.Default);

    public static TValue DeserializeJson<TValue>(string json) => JsonSerializer.Deserialize(json, (JsonTypeInfo<TValue>)ErrandeskJsonSerializerContext.Default.GetTypeInfo(typeof(TValue))!)!;
}
