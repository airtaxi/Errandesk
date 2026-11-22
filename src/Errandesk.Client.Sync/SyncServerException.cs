namespace Errandesk.Client.Sync;

/// <summary>
/// The server answered a read of the synchronization with an error status.
/// </summary>
public sealed class SyncServerException(int statusCode) : Exception($"The server responded with status {statusCode}.")
{
    public int StatusCode { get; } = statusCode;
}
