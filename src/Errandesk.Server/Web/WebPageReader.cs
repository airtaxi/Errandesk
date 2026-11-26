using System.Net;
using Errandesk.Server.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Errandesk.Server.Web;

/// <summary>
/// Reads web pages for the assistant: downloads a page with size, time and type limits, follows redirects itself, extracts its text,
/// and returns it in parts. Extracted pages are kept for a few minutes per user so that reading further parts does not download the page again.
/// </summary>
public sealed class WebPageReader(IHttpClientFactory httpClientFactory) : IDisposable
{
    public const string HttpClientName = "web-fetch";
    public const string UserAgent = "Mozilla/5.0 (compatible; Errandesk)";
    public const int MaximumRedirectCount = 5;
    public const int MaximumResponseByteCount = 5 * 1024 * 1024;
    public const int DefaultPartLength = 8000;
    public const int MinimumPartLength = 1000;
    public const int MaximumPartLength = 20000;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const long CacheSizeLimit = 64L * 1024 * 1024;
    private const int ReadBufferSize = 81920;

    private static readonly HashSet<string> s_htmlMediaTypes = new(["text/html", "application/xhtml+xml"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_textMediaTypes = new(["text/plain", "text/markdown", "text/x-markdown", "application/json", "application/xml", "text/xml"], StringComparer.OrdinalIgnoreCase);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });

    /// <summary>Returns the part of the page text that starts at <paramref name="startIndex"/>, at most <paramref name="maxLength"/> characters long.</summary>
    /// <exception cref="RequestValidationException">The URL or the start index is invalid.</exception>
    /// <exception cref="WebPageUnavailableException">The page could not be read.</exception>
    public async Task<WebPageContent> ReadAsync(Guid userId, string url, int startIndex, int maxLength, CancellationToken cancellationToken)
    {
        if (!TryParseAddress(url?.Trim(), out var address)) throw new RequestValidationException("The URL must be an absolute http or https address without a user name or password.");

        var cacheKey = (userId, address.AbsoluteUri);
        if (!_cache.TryGetValue(cacheKey, out WebPageDocument? document) || document is null)
        {
            document = await DownloadAsync(address, cancellationToken);
            _cache.Set(cacheKey, document, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration, Size = Math.Max(1, document.Text.Length * sizeof(char)) });
        }

        return CreatePart(document, startIndex, maxLength);
    }

    /// <summary>Cuts a part of at most <paramref name="maxLength"/> characters, clamped to the allowed part lengths, without splitting a surrogate pair.</summary>
    public static WebPageContent CreatePart(WebPageDocument document, int startIndex, int maxLength)
    {
        var text = document.Text;
        if (startIndex < 0 || (startIndex > 0 && startIndex >= text.Length)) throw new RequestValidationException($"The start index must be from 0 to {Math.Max(0, text.Length - 1)}.");

        var endIndex = Math.Min(text.Length, startIndex + Math.Clamp(maxLength, MinimumPartLength, MaximumPartLength));
        if (endIndex < text.Length && char.IsHighSurrogate(text[endIndex - 1])) endIndex--;
        return new WebPageContent(document.Url, document.Title, text[startIndex..endIndex], startIndex, text.Length, endIndex < text.Length ? endIndex : null);
    }

    public void Dispose() => _cache.Dispose();

    private async Task<WebPageDocument> DownloadAsync(Uri address, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RequestTimeout);
        try { return await DownloadWithinTimeoutAsync(address, timeoutSource.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new WebPageUnavailableException("The page did not answer in time."); }
    }

    private async Task<WebPageDocument> DownloadWithinTimeoutAsync(Uri address, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var currentAddress = address;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentAddress);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/markdown;q=0.9,text/plain;q=0.9,application/json;q=0.8,application/xml;q=0.8,*/*;q=0.1");
            using var response = await SendAsync(httpClient, request, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaximumRedirectCount) throw new WebPageUnavailableException($"The page redirected more than {MaximumRedirectCount} times.");
                if (response.Headers.Location is not { } location) throw new WebPageUnavailableException("The page redirected without giving a new address.");
                if (!TryParseAddress(new Uri(currentAddress, location).AbsoluteUri, out var redirectAddress)) throw new WebPageUnavailableException("The page redirected to an address that is not an http or https URL.");
                currentAddress = redirectAddress;
                continue;
            }

            if (!response.IsSuccessStatusCode) throw new WebPageUnavailableException($"The server answered with status {(int)response.StatusCode}.");

            var contentType = response.Content.Headers.ContentType;
            var mediaType = contentType?.MediaType ?? "text/html";
            var isHtml = s_htmlMediaTypes.Contains(mediaType);
            if (!isHtml && !s_textMediaTypes.Contains(mediaType)) throw new WebPageUnavailableException($"The page is of type {mediaType}. Only HTML, plain text, Markdown, JSON and XML pages can be read.");

            var bytes = await ReadBodyAsync(response.Content, cancellationToken);
            var text = WebPageTextDecoder.Decode(bytes, contentType?.CharSet, isHtml);
            return isHtml ? WebPageTextExtractor.Extract(currentAddress, text) : new WebPageDocument(currentAddress.AbsoluteUri, null, text.Trim());
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient httpClient, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException exception) when (FindBlockedAddressException(exception) is { } blockedAddressException) { throw new WebPageUnavailableException(blockedAddressException.Message); }
        catch (HttpRequestException exception) { throw new WebPageUnavailableException($"The page could not be reached ({exception.HttpRequestError})."); }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseByteCount) throw CreateTooLargeException();

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferSize];
        int readCount;
        while ((readCount = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + readCount > MaximumResponseByteCount) throw CreateTooLargeException();
            buffer.Write(chunk, 0, readCount);
        }
        return buffer.ToArray();
    }

    private static WebPageUnavailableException CreateTooLargeException() => new($"The page is larger than {MaximumResponseByteCount / (1024 * 1024)} MB.");

    private static WebFetchBlockedAddressException? FindBlockedAddressException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebFetchBlockedAddressException blockedAddressException)
            {
                return blockedAddressException;
            }
        }
        return null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool TryParseAddress(string? url, out Uri address)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsedAddress) && (parsedAddress.Scheme == Uri.UriSchemeHttp || parsedAddress.Scheme == Uri.UriSchemeHttps) && parsedAddress.UserInfo.Length == 0)
        {
            address = parsedAddress;
            return true;
        }

        address = null!;
        return false;
    }
}
