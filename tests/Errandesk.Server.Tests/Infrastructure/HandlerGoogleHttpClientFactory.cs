using Google.Apis.Http;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Makes the Google and Firebase client libraries send their HTTP requests through one message handler.
/// </summary>
public sealed class HandlerGoogleHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
}
