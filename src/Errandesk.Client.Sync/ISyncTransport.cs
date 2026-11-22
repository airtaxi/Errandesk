using Errandesk.Contracts;

namespace Errandesk.Client.Sync;

/// <summary>
/// The requests the synchronization makes to the server.
/// </summary>
/// <remarks>
/// Implementations throw <see cref="HttpRequestException"/> when the server cannot be reached and
/// <see cref="TaskCanceledException"/> when a request times out.
/// </remarks>
public interface ISyncTransport
{
    /// <summary>
    /// Sends an outbox operation with its operation ID as the idempotency key.
    /// </summary>
    Task<SyncTransportResponse> SendAsync(OutboxOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one item, such as <c>api/tasks/{id}</c>. The status is 404 when the item no longer exists.
    /// </summary>
    Task<SyncTransportResponse> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the lists, tasks and notes that changed after <paramref name="since"/>.
    /// </summary>
    /// <exception cref="SyncServerException">The server answered with an error status.</exception>
    Task<ChangesResponse> GetChangesAsync(long since, CancellationToken cancellationToken);
}
