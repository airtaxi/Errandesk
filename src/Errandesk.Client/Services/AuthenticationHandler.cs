using System.Net;
using System.Net.Http.Headers;
using Errandesk.Client.Managers;

namespace Errandesk.Client.Services;

/// <summary>
/// Attaches the access token to each request. When the server answers 401, refreshes the token once and repeats the request;
/// when the refresh fails, the session ends and the request throws <see cref="SessionExpiredException"/>.
/// </summary>
public sealed class AuthenticationHandler(SessionManager sessionManager) : DelegatingHandler
{
    private const string BearerScheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null) await request.Content.LoadIntoBufferAsync(cancellationToken);

        var accessToken = await sessionManager.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        if (!await sessionManager.RefreshAsync(accessToken, cancellationToken)) throw new SessionExpiredException();

        var retryRequest = CloneRequest(request);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, await sessionManager.GetAccessTokenAsync(cancellationToken));
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clonedRequest = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            if (header.Key != "Authorization")
            {
                clonedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clonedRequest;
    }
}
