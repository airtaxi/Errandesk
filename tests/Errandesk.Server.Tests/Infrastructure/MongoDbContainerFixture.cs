using Testcontainers.MongoDb;

[assembly: AssemblyFixture(typeof(Errandesk.Server.Tests.Infrastructure.MongoDbContainerFixture))]

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Starts one MongoDB container for the whole test run. Each test uses its own database inside it.
/// </summary>
public sealed class MongoDbContainerFixture : IAsyncLifetime
{
    public const string MongoDbImage = "mongo:9";

    private readonly MongoDbContainer _container = new MongoDbBuilder(MongoDbImage).Build();

    public string ConnectionString => _container.GetConnectionString();

    public static string CreateDatabaseName() => $"errandesk-test-{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
