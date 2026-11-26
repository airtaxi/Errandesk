using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Errandesk.Server.Web;

namespace Errandesk.Server.Tests.Web;

public sealed class WebPageReaderTests
{
    private static readonly Guid s_userId = Guid.CreateVersion7();

    private static readonly string s_articleHtml = $$"""
        <html>
        <head><title>Spring cleaning guide</title><script>alert('x')</script><style>body { color: red; }</style></head>
        <body>
        <nav><a href="/">Home</a></nav>
        <article>
        <h1>Spring cleaning guide</h1>
        {{string.Concat(Enumerable.Range(1, 8).Select(index => $"<p>Step {index}: clear one shelf at a time, wipe it down, and put back only the things you used this season. Keep a box for donations near the door.</p>"))}}
        <p>Read the <a href="/checklist">full checklist</a>.</p>
        </article>
        </body>
        </html>
        """;

    [Fact]
    public async Task ReadAsync_ReturnsTheArticleAsMarkdownWithoutScripts()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateResponse(s_articleHtml, "text/html"));
        using var reader = CreateReader(handler);

        var content = await reader.ReadAsync(s_userId, "https://example.com/guide", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken);

        Assert.Equal("https://example.com/guide", content.Url);
        Assert.Equal("Spring cleaning guide", content.Title);
        Assert.Contains("Step 8: clear one shelf", content.Content, StringComparison.Ordinal);
        Assert.Contains("[full checklist](https://example.com/checklist)", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("color: red", content.Content, StringComparison.Ordinal);
        Assert.Null(content.NextStartIndex);
        Assert.Contains("Errandesk", Assert.Single(handler.Requests).Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_KeepsTheExtractedPageForLaterParts()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateResponse(new string('a', 2500), "text/plain"));
        using var reader = CreateReader(handler);

        var first = await reader.ReadAsync(s_userId, "https://example.com/long.txt", 0, 1000, TestContext.Current.CancellationToken);
        var second = await reader.ReadAsync(s_userId, "https://example.com/long.txt", first.NextStartIndex!.Value, 1000, TestContext.Current.CancellationToken);
        var third = await reader.ReadAsync(s_userId, "https://example.com/long.txt", second.NextStartIndex!.Value, 1000, TestContext.Current.CancellationToken);

        Assert.Equal([1000, 2000], [first.NextStartIndex.Value, second.NextStartIndex.Value]);
        Assert.Equal(500, third.Content.Length);
        Assert.Null(third.NextStartIndex);
        Assert.Equal(2500, third.TotalLength);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReadAsync_FollowsRelativeRedirects()
    {
        var handler = new RecordingHttpMessageHandler(request => request.Uri.AbsolutePath == "/old" ? CreateRedirect("/new") : CreateResponse("moved here", "text/plain"));
        using var reader = CreateReader(handler);

        var content = await reader.ReadAsync(s_userId, "https://example.com/old", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken);

        Assert.Equal("https://example.com/new", content.Url);
        Assert.Equal("moved here", content.Content);
    }

    [Fact]
    public async Task ReadAsync_StopsAfterTooManyRedirects()
    {
        var handler = new RecordingHttpMessageHandler(request => CreateRedirect($"/page{request.Uri.AbsolutePath.Length}"));
        using var reader = CreateReader(handler);

        var exception = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "https://example.com/start", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));

        Assert.Contains("redirected more than", exception.Message, StringComparison.Ordinal);
        Assert.Equal(WebPageReader.MaximumRedirectCount + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadAsync_RefusesRedirectsToOtherSchemes()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateRedirect("file:///etc/passwd"));
        using var reader = CreateReader(handler);

        await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "https://example.com/start", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_RefusesUnsupportedContentTypes()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateResponse("PNG", "image/png"));
        using var reader = CreateReader(handler);

        var exception = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "https://example.com/image.png", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));

        Assert.Contains("image/png", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RefusesPagesLargerThanTheLimit()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[WebPageReader.MaximumResponseByteCount + 1])) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } } });
        using var reader = CreateReader(handler);

        var exception = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "https://example.com/huge.txt", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));

        Assert.Contains("larger than", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ReportsErrorStatuses()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var reader = CreateReader(handler);

        var exception = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "https://example.com/missing", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));

        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("https://user:secret@example.com/")]
    public async Task ReadAsync_RefusesInvalidUrls(string url)
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateResponse("never", "text/plain"));
        using var reader = CreateReader(handler);

        await Assert.ThrowsAsync<RequestValidationException>(() => reader.ReadAsync(s_userId, url, 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadAsync_DecodesTheCharsetOfTheMetaTag()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding("euc-kr").GetBytes("<html><head><meta charset=\"euc-kr\"><title>장보기</title></head><body><p>우유와 달걀을 산다.</p></body></html>");
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("text/html") } } });
        using var reader = CreateReader(handler);

        var content = await reader.ReadAsync(s_userId, "https://example.kr/", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken);

        Assert.Equal("장보기", content.Title);
        Assert.Contains("우유와 달걀을 산다.", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ReportsBlockedAddresses()
    {
        using var reader = new WebPageReader(new HandlerHttpClientFactory(WebFetchConnector.CreateHandler(false)));

        var exception = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(s_userId, "http://127.0.0.1:9/", 0, WebPageReader.DefaultPartLength, TestContext.Current.CancellationToken));

        Assert.Contains("local or private network", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePart_DoesNotSplitSurrogatePairs()
    {
        var document = new WebPageDocument("https://example.com/", null, new string('a', 999) + "😀" + new string('b', 100));

        var part = WebPageReader.CreatePart(document, 0, 1000);

        Assert.Equal(999, part.Content.Length);
        Assert.Equal(999, part.NextStartIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(11)]
    public void CreatePart_RefusesStartIndexesOutsideTheText(int startIndex) => Assert.Throws<RequestValidationException>(() => WebPageReader.CreatePart(new WebPageDocument("https://example.com/", null, "0123456789"), startIndex, 1000));

    [Fact]
    public void CreatePart_ReturnsAnEmptyPartOfAnEmptyPage()
    {
        var part = WebPageReader.CreatePart(new WebPageDocument("https://example.com/", null, ""), 0, 1000);

        Assert.Equal("", part.Content);
        Assert.Null(part.NextStartIndex);
    }

    private static WebPageReader CreateReader(HttpMessageHandler handler)
    {
        var httpClientFactory = new HandlerHttpClientFactory(handler);
        return new WebPageReader(new UserAgentHttpClientFactory(httpClientFactory));
    }

    private static HttpResponseMessage CreateResponse(string content, string mediaType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, mediaType)
    };

    private static HttpResponseMessage CreateRedirect(string location) => new(HttpStatusCode.Found)
    {
        Headers =
        {
            Location = new Uri(location, UriKind.RelativeOrAbsolute)
        }
    };

    /// <summary>Adds the user agent the server configures for the web page client.</summary>
    private sealed class UserAgentHttpClientFactory(IHttpClientFactory innerFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var httpClient = innerFactory.CreateClient(name);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(WebPageReader.UserAgent);
            return httpClient;
        }
    }
}
