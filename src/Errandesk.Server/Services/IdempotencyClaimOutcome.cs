namespace Errandesk.Server.Services;

/// <summary>
/// What happens to a request that carries an <c>Idempotency-Key</c>.
/// </summary>
public enum IdempotencyClaimOutcome
{
    /// <summary>The key is new or was abandoned; the request runs and its response is stored.</summary>
    Started,

    /// <summary>The key completed earlier; its stored response is sent again.</summary>
    Replay,

    /// <summary>Another request with the key is still running.</summary>
    InProgress,

    /// <summary>The key was used for a different request.</summary>
    Mismatch,
}
