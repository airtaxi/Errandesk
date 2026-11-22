using System.Net.Http.Headers;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Attaches the access token returned by <paramref name="getAccessToken"/> to every request, or no Authorization header when it is empty.
/// </summary>
public sealed class BearerTokenHandler(Func<string> getAccessToken) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = getAccessToken();
        if (accessToken.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return base.SendAsync(request, cancellationToken);
    }
}
