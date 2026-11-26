using Errandesk.Server.Configuration;

namespace Errandesk.Server.Web;

public static class WebFetchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the web page reader of the assistant with its HTTP client when the assistant is configured and reading web pages is enabled.
    /// </summary>
    public static IServiceCollection AddErrandeskWebFetch(this IServiceCollection services, ServerSettings settings)
    {
        if (settings.Ai is not { IsWebFetchEnabled: true } aiSettings) return services;

        services.AddHttpClient(WebPageReader.HttpClientName, httpClient =>
        {
            httpClient.Timeout = WebPageReader.RequestTimeout;
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(WebPageReader.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(() => WebFetchConnector.CreateHandler(aiSettings.AllowsPrivateNetworkWebFetch));
        services.AddSingleton<WebPageReader>();
        return services;
    }
}
