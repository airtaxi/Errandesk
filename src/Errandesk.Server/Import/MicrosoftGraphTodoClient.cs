using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Errandesk.Server.Import;

/// <summary>
/// Reads a user's Microsoft To Do lists, tasks and checklist items through Microsoft Graph, following every <c>@odata.nextLink</c>.
/// Refreshes the access token once per request when Graph answers 401, and waits and tries again when Graph throttles a request.
/// </summary>
public sealed class MicrosoftGraphTodoClient(HttpClient httpClient, MicrosoftTokens tokens, Func<string, CancellationToken, Task<MicrosoftTokens>> refreshTokensAsync, MicrosoftTodoImportOptions options)
{
    public const string HttpClientName = "microsoft-graph";

    public static readonly Uri BaseAddress = new("https://graph.microsoft.com/v1.0/");

    private const int MaximumPageCount = 10_000;

    private MicrosoftTokens _tokens = tokens;

    public Task<IReadOnlyList<GraphTodoTaskList>> GetListsAsync(CancellationToken cancellationToken) => GetAllPagesAsync(new Uri(BaseAddress, "me/todo/lists"), MicrosoftGraphJsonSerializerContext.Default.GraphCollectionPageGraphTodoTaskList, cancellationToken);

    public Task<IReadOnlyList<GraphTodoTask>> GetTasksAsync(string listId, CancellationToken cancellationToken) => GetAllPagesAsync(new Uri(BaseAddress, $"me/todo/lists/{Uri.EscapeDataString(listId)}/tasks"), MicrosoftGraphJsonSerializerContext.Default.GraphCollectionPageGraphTodoTask, cancellationToken);

    public Task<IReadOnlyList<GraphChecklistItem>> GetChecklistItemsAsync(string listId, string taskId, CancellationToken cancellationToken) => GetAllPagesAsync(new Uri(BaseAddress, $"me/todo/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}/checklistItems"), MicrosoftGraphJsonSerializerContext.Default.GraphCollectionPageGraphChecklistItem, cancellationToken);

    private async Task<IReadOnlyList<TItem>> GetAllPagesAsync<TItem>(Uri firstPageAddress, JsonTypeInfo<GraphCollectionPage<TItem>> typeInfo, CancellationToken cancellationToken)
    {
        List<TItem> items = [];
        var pageAddress = firstPageAddress;
        for (var pageCount = 1; ; pageCount++)
        {
            var page = await GetPageAsync(pageAddress, typeInfo, cancellationToken);
            items.AddRange(page.Value ?? []);
            if (string.IsNullOrEmpty(page.NextLink)) return items;
            if (pageCount == MaximumPageCount) throw new MicrosoftServiceException("Microsoft Graph returned too many pages.");

            pageAddress = Uri.TryCreate(page.NextLink, UriKind.Absolute, out var nextPageAddress) && nextPageAddress.Scheme == Uri.UriSchemeHttps && string.Equals(nextPageAddress.Host, BaseAddress.Host, StringComparison.OrdinalIgnoreCase) ? nextPageAddress : throw new MicrosoftServiceException("Microsoft Graph returned an unexpected next page address.");
        }
    }

    private async Task<GraphCollectionPage<TItem>> GetPageAsync<TItem>(Uri pageAddress, JsonTypeInfo<GraphCollectionPage<TItem>> typeInfo, CancellationToken cancellationToken)
    {
        var hasRefreshedTokens = false;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageAddress);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !hasRefreshedTokens && _tokens.RefreshToken is { } refreshToken)
            {
                _tokens = await refreshTokensAsync(refreshToken, cancellationToken);
                hasRefreshedTokens = true;
                continue;
            }

            if (IsTransient(response.StatusCode) && attempt < options.MaximumRequestAttempts)
            {
                await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode) throw new MicrosoftServiceException($"Microsoft Graph answered with status {(int)response.StatusCode}.");
            try { return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken) ?? throw new MicrosoftServiceException("Microsoft Graph returned an empty response."); }
            catch (JsonException) { throw new MicrosoftServiceException("Microsoft Graph returned a response that could not be read."); }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or HttpStatusCode.BadGateway;

    /// <summary>The wait the response asks for in <c>Retry-After</c>, otherwise a doubling wait from 1 second, never longer than <see cref="MicrosoftTodoImportOptions.MaximumRetryDelay"/>.</summary>
    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var requestedDelay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } retryDate ? retryDate - DateTimeOffset.UtcNow : (TimeSpan?)null);
        var delay = requestedDelay ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > options.MaximumRetryDelay ? options.MaximumRetryDelay : delay;
    }
}
