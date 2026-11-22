using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class ServerInfoEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string ServerInfoPath = "/api/server-info";

    [Fact]
    public async Task GetServerInfo_WithDefaults_UsesCamelCaseNamesAndStringEnums()
    {
        await using var application = await StartAsync();

        using var response = await application.Client.GetAsync(ServerInfoPath, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;

        Assert.Equal(ServerInfoService.ServerVersion, root.GetProperty("serverVersion").GetString());
        Assert.Equal("invite", root.GetProperty("registrationMode").GetString());
        Assert.True(root.GetProperty("isRegistrationAvailable").GetBoolean());
        Assert.False(root.GetProperty("isAiAvailable").GetBoolean());
        Assert.False(root.GetProperty("isAndroidPushAvailable").GetBoolean());
        Assert.False(root.GetProperty("isIosPushAvailable").GetBoolean());
    }

    [Fact]
    public async Task GetServerInfo_DeserializesWithContractContext()
    {
        await using var application = await StartAsync(new Dictionary<string, string> { [ServerEnvironmentVariables.RegistrationMode] = "open" });

        var serverInfo = await application.Client.GetFromJsonAsync(ServerInfoPath, ErrandeskJsonSerializerContext.Default.ServerInfoResponse, TestContext.Current.CancellationToken);

        Assert.NotNull(serverInfo);
        Assert.Equal(RegistrationMode.Open, serverInfo.RegistrationMode);
        Assert.True(serverInfo.IsRegistrationAvailable);
        Assert.Matches(@"^\d+\.\d+\.\d+", serverInfo.ServerVersion);
    }

    [Fact]
    public async Task GetServerInfo_WithDisabledRegistration_AllowsRegistrationOnlyUntilTheFirstUserExists()
    {
        await using var application = await StartAsync(new Dictionary<string, string> { [ServerEnvironmentVariables.RegistrationMode] = "disabled" });

        var beforeFirstUser = await GetServerInfoAsync(application);
        await application.Services.GetRequiredService<IMongoDatabase>().GetCollection<BsonDocument>(MongoCollectionNames.Users).InsertOneAsync(new BsonDocument("userName", "first"), cancellationToken: TestContext.Current.CancellationToken);
        var afterFirstUser = await GetServerInfoAsync(application);

        Assert.Equal(RegistrationMode.Disabled, beforeFirstUser.RegistrationMode);
        Assert.True(beforeFirstUser.IsRegistrationAvailable);
        Assert.False(afterFirstUser.IsRegistrationAvailable);
    }

    [Fact]
    public async Task GetServerInfo_WithAiAndPushConfigured_ReportsCapabilities()
    {
        await using var application = await StartAsync(new Dictionary<string, string>
        {
            [ServerEnvironmentVariables.AiEndpoint] = "http://localhost:11434/v1",
            [ServerEnvironmentVariables.AiModel] = "llama3.2",
            [ServerEnvironmentVariables.FcmProjectId] = "errandesk-test",
            [ServerEnvironmentVariables.FcmClientEmail] = "push@errandesk-test.iam.gserviceaccount.com",
            [ServerEnvironmentVariables.FcmPrivateKey] = TestKeys.CreateRsaPrivateKeyPem(),
            [ServerEnvironmentVariables.ApnsTeamId] = "TEAM123456",
            [ServerEnvironmentVariables.ApnsKeyId] = "KEY1234567",
            [ServerEnvironmentVariables.ApnsPrivateKey] = TestKeys.CreateEllipticCurvePrivateKeyPem(),
            [ServerEnvironmentVariables.ApnsBundleId] = "com.airtaxi.errandesk",
        });

        var serverInfo = await GetServerInfoAsync(application);

        Assert.True(serverInfo.IsAiAvailable);
        Assert.True(serverInfo.IsAndroidPushAvailable);
        Assert.True(serverInfo.IsIosPushAvailable);
    }

    [Fact]
    public async Task GetServerInfo_WithDisabledRegistrationAndUnreachableDatabase_ReturnsProblemDetails()
    {
        var settings = TestServerSettings.Create(UnreachableMongoDb.CreateConnectionString(), MongoDbContainerFixture.CreateDatabaseName(), new Dictionary<string, string> { [ServerEnvironmentVariables.RegistrationMode] = "disabled" });
        await using var application = await TestApplication.StartAsync(settings);

        using var response = await application.Client.GetAsync(ServerInfoPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetUnknownApiPath_ReturnsProblemDetails()
    {
        await using var application = await StartAsync();

        using var response = await application.Client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetOpenApiDocument_IsAvailableOnlyInDevelopment()
    {
        var settings = TestServerSettings.Create(mongoDb.ConnectionString, MongoDbContainerFixture.CreateDatabaseName());
        await using var developmentApplication = await TestApplication.StartDevelopmentAsync(settings);
        await using var productionApplication = await TestApplication.StartAsync(settings);

        using var developmentResponse = await developmentApplication.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var productionResponse = await productionApplication.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, developmentResponse.StatusCode);
        Assert.Contains("/api/server-info", await developmentResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, productionResponse.StatusCode);
    }

    private Task<TestApplication> StartAsync(IReadOnlyDictionary<string, string>? additionalVariables = null) => TestApplication.StartAsync(TestServerSettings.Create(mongoDb.ConnectionString, MongoDbContainerFixture.CreateDatabaseName(), additionalVariables));

    private static async Task<ServerInfoResponse> GetServerInfoAsync(TestApplication application)
    {
        var serverInfo = await application.Client.GetFromJsonAsync(ServerInfoPath, ErrandeskJsonSerializerContext.Default.ServerInfoResponse, TestContext.Current.CancellationToken);
        Assert.NotNull(serverInfo);
        return serverInfo;
    }
}
