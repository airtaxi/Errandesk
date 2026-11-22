using Microsoft.Extensions.Time.Testing;

namespace Errandesk.Client.Sync.Tests.Infrastructure;

/// <summary>
/// A local store in a temporary database file with a data service on a controllable clock. Disposing deletes the file.
/// </summary>
public sealed class TemporaryDataService : IDisposable
{
    public static readonly DateTimeOffset DefaultStartTime = new(2031, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "errandesk-sync-tests", $"{Guid.NewGuid():N}.db");

    public TemporaryDataService()
    {
        Store = LocalStore.Open(_databasePath);
        Data = new LocalDataService(Store, TimeProvider);
    }

    public FakeTimeProvider TimeProvider { get; } = new(DefaultStartTime);

    public LocalStore Store { get; }

    public LocalDataService Data { get; }

    public DateOnly Today => Data.Today;

    public void Dispose()
    {
        Store.Dispose();
        LocalStore.Delete(_databasePath);
    }
}
