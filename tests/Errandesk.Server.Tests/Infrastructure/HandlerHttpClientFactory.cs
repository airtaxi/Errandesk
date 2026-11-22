namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Creates HTTP clients that send through one message handler.
/// </summary>
public sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, false);
}
