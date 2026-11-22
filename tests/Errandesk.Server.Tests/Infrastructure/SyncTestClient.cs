using Errandesk.Client.Sync;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// A device of a test user: a local store in a temporary file, synchronized with the test server over HTTP. Disposing deletes the file.
/// </summary>
public sealed class SyncTestClient : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "errandesk-sync-integration-tests", $"{Guid.NewGuid():N}.db");
    private readonly HttpClient _httpClient;

    public SyncTestClient(ApiTestServer server, TestUser user)
    {
        Store = LocalStore.Open(_databasePath);
        Data = new LocalDataService(Store, server.TimeProvider);
        _httpClient = server.CreateHttpClient(user);
        Transport = new SwitchableSyncTransport(new HttpSyncTransport(_httpClient));
        Engine = new SyncEngine(Data, Transport);
    }

    public LocalStore Store { get; }

    public LocalDataService Data { get; }

    public SwitchableSyncTransport Transport { get; }

    public SyncEngine Engine { get; }

    public Task<SyncOutcome> SyncAsync() => Engine.SyncAsync(TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _httpClient.Dispose();
        Store.Dispose();
        LocalStore.Delete(_databasePath);
    }
}
