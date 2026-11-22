namespace Errandesk.Client.Sync;

/// <summary>
/// What the server's answer to an outbox operation means.
/// </summary>
internal enum OperationResult
{
    /// <summary>The server applied the operation, or the item it deletes is already gone.</summary>
    Acknowledged,

    /// <summary>The server refused the operation for good; it is dropped and its item read again.</summary>
    Rejected,

    /// <summary>The operation stays first in the outbox and is sent again later.</summary>
    RetryLater,
}
