namespace Errandesk.Client.Sync;

/// <summary>
/// How a synchronization run ended.
/// </summary>
public enum SyncOutcome
{
    /// <summary>Every pending change was sent and the latest changes of the server were applied.</summary>
    Completed,

    /// <summary>The server could not be reached or is unavailable. Pending changes stay in the outbox.</summary>
    Offline,

    /// <summary>The server asked to try again later, for example because an item was being changed by another request.</summary>
    RetryLater,
}
