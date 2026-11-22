using Errandesk.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Runs the server pipeline in memory on a test server with the given settings.
/// </summary>
public sealed class TestApplication(WebApplication application) : IAsyncDisposable
{
    public HttpClient Client { get; } = application.GetTestClient();

    /// <summary>
    /// Creates a client whose requests pass through <paramref name="handler"/> before they reach the server.
    /// </summary>
    public HttpClient CreateClient(DelegatingHandler handler)
    {
        var testServer = application.GetTestServer();
        handler.InnerHandler = testServer.CreateHandler();
        return new HttpClient(handler) { BaseAddress = testServer.BaseAddress };
    }

    public IServiceProvider Services => application.Services;

    /// <param name="settings">Server settings.</param>
    /// <param name="environmentName">Hosting environment name.</param>
    /// <param name="configureServices">Replaces or adds services after the server registered its own.</param>
    public static async Task<TestApplication> StartAsync(ServerSettings settings, string environmentName = "Production", Action<IServiceCollection>? configureServices = null)
    {
        var builder = ErrandeskServerApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName }, settings);
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);
        var application = ErrandeskServerApplication.Build(builder);
        await application.StartAsync(TestContext.Current.CancellationToken);
        return new TestApplication(application);
    }

    public static Task<TestApplication> StartDevelopmentAsync(ServerSettings settings) => StartAsync(settings, Environments.Development);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.StopAsync();
        await application.DisposeAsync();
    }
}
