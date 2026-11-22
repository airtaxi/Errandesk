using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Errandesk.Client.Services;

/// <summary>
/// Reads server responses: success bodies through the contracts serializer context, errors as <see cref="ApiException"/>.
/// </summary>
public static class ApiResponseReader
{
    private const string ProblemDetailPropertyName = "detail";

    /// <exception cref="ApiException">The response has an error status.</exception>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? problemDetail = null;
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(ProblemDetailPropertyName, out var detailElement) && detailElement.ValueKind == JsonValueKind.String) problemDetail = detailElement.GetString();
        }
        catch (JsonException) { }

        throw new ApiException(response.StatusCode, problemDetail);
    }

    /// <exception cref="ApiException">The response has an error status.</exception>
    /// <exception cref="InvalidDataException">The body is not the expected JSON.</exception>
    public static async Task<TResponse> ReadAsync<TResponse>(HttpResponseMessage response, JsonTypeInfo<TResponse> typeInfo, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        TResponse? value;
        try { value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) { throw new InvalidDataException("The response body is not the expected JSON.", exception); }

        return value ?? throw new InvalidDataException("The response body is empty.");
    }
}
