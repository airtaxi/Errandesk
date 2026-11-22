using System.Net;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class HealthEndpointTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task GetHealth_WhenMongoDbAnswers_ReturnsOk()
    {
        await using var application = await TestApplication.StartAsync(TestServerSettings.Create(mongoDb.ConnectionString, MongoDbContainerFixture.CreateDatabaseName()));

        using var response = await application.Client.GetAsync(ErrandeskServerApplication.HealthPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_WhenMongoDbIsUnreachable_ReturnsServiceUnavailable()
    {
        await using var application = await TestApplication.StartAsync(TestServerSettings.Create(UnreachableMongoDb.CreateConnectionString(), MongoDbContainerFixture.CreateDatabaseName()));

        using var response = await application.Client.GetAsync(ErrandeskServerApplication.HealthPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
