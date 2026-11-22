using System.Text.Json;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests.Infrastructure;

/// <summary>
/// A transport that records requests and answers them from scripted functions instead of a server.
/// </summary>
public sealed class ScriptedSyncTransport : ISyncTransport
{
    public List<OutboxOperation> SentOperations { get; } = [];

    public List<string> ReadPaths { get; } = [];

    public List<long> ChangeRequests { get; } = [];

    /// <summary>When set, every request fails as if the server could not be reached.</summary>
    public bool IsOffline { get; set; }

    public Func<OutboxOperation, SyncTransportResponse> RespondToOperation { get; set; } = _ => new SyncTransportResponse(204, null);

    public Func<string, SyncTransportResponse> RespondToRead { get; set; } = _ => new SyncTransportResponse(404, null);

    public Func<long, ChangesResponse> RespondToChanges { get; set; } = since => TestData.Changes(Math.Max(since, 1));

    /// <summary>Runs before a scripted answer is returned, for example to change the local data while a request is in flight.</summary>
    public Action? BeforeResponse { get; set; }

    public Task<SyncTransportResponse> SendAsync(OutboxOperation operation, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        SentOperations.Add(operation);
        BeforeResponse?.Invoke();
        return Task.FromResult(RespondToOperation(operation));
    }

    public Task<SyncTransportResponse> GetAsync(string path, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        ReadPaths.Add(path);
        return Task.FromResult(RespondToRead(path));
    }

    public async Task<ChangesResponse> GetChangesAsync(long since, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        ChangeRequests.Add(since);
        await Task.Yield();
        return RespondToChanges(since);
    }

    public static SyncTransportResponse Json<T>(int statusCode, T value) => new(statusCode, JsonSerializer.Serialize(value, typeof(T), ErrandeskJsonSerializerContext.Default));

    private void ThrowWhenOffline()
    {
        if (IsOffline)
        {
            throw new HttpRequestException("No connection could be made.");
        }
    }
}
