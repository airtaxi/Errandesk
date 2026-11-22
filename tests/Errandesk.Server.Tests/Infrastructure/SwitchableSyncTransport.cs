using Errandesk.Client.Sync;
using Errandesk.Contracts;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Passes the requests of the synchronization to the server, except while it simulates a lost connection.
/// </summary>
public sealed class SwitchableSyncTransport(ISyncTransport transport) : ISyncTransport
{
    /// <summary>When set, requests fail before they reach the server.</summary>
    public bool IsOffline { get; set; }

    /// <summary>When set, operations reach the server but their responses are lost on the way back.</summary>
    public bool LosesResponses { get; set; }

    public int SentOperationCount { get; private set; }

    public async Task<SyncTransportResponse> SendAsync(OutboxOperation operation, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        SentOperationCount++;
        var response = await transport.SendAsync(operation, cancellationToken);
        if (LosesResponses) throw new HttpRequestException("The connection was lost before the response arrived.");
        return response;
    }

    public Task<SyncTransportResponse> GetAsync(string path, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        return transport.GetAsync(path, cancellationToken);
    }

    public Task<ChangesResponse> GetChangesAsync(long since, CancellationToken cancellationToken)
    {
        ThrowWhenOffline();
        return transport.GetChangesAsync(since, cancellationToken);
    }

    private void ThrowWhenOffline()
    {
        if (IsOffline)
        {
            throw new HttpRequestException("No connection could be made.");
        }
    }
}
