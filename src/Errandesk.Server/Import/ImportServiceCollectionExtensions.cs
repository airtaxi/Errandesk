namespace Errandesk.Server.Import;

public static class ImportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Microsoft To Do import with its HTTP clients for the Microsoft identity platform and Microsoft Graph.
    /// </summary>
    public static IServiceCollection AddErrandeskImport(this IServiceCollection services)
    {
        services.AddHttpClient(MicrosoftIdentityClient.HttpClientName, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(MicrosoftGraphTodoClient.HttpClientName, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<MicrosoftTodoImportOptions>();
        services.AddSingleton<MicrosoftIdentityClient>();
        services.AddSingleton<MicrosoftTodoImporter>();
        services.AddSingleton<MicrosoftTodoImportService>();
        return services;
    }
}
