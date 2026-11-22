using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync;

/// <summary>
/// Makes the requests of the synchronization over HTTP. Paths are relative to the client's <see cref="HttpClient.BaseAddress"/>,
/// which must end with a slash; the client's handlers attach the credentials.
/// </summary>
public sealed class HttpSyncTransport(HttpClient httpClient) : ISyncTransport
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private const string JsonMediaType = "application/json";

    public async Task<SyncTransportResponse> SendAsync(OutboxOperation operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.Path);
        request.Headers.TryAddWithoutValidation(IdempotencyKeyHeaderName, operation.OperationId.ToString());
        if (operation.Body is not null) request.Content = new StringContent(operation.Body, Encoding.UTF8, JsonMediaType);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<SyncTransportResponse> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<ChangesResponse> GetChangesAsync(long since, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/changes?since={since.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SyncServerException((int)response.StatusCode);
        return await response.Content.ReadFromJsonAsync(ErrandeskJsonSerializerContext.Default.ChangesResponse, cancellationToken) ?? throw new InvalidDataException("The change feed response is empty.");
    }

    private static async Task<SyncTransportResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SyncTransportResponse((int)response.StatusCode, body.Length == 0 ? null : body, response.Headers.RetryAfter is not null);
    }
}
