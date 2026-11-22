namespace Errandesk.Server.Import;

/// <summary>
/// The outcome of one token request of the device code flow.
/// </summary>
public enum DeviceCodeTokenStatus
{
    /// <summary>The user has not finished signing in; ask again after the interval.</summary>
    Pending,

    /// <summary>The client asks too often; ask again after a longer interval.</summary>
    SlowDown,

    Declined,

    Expired,

    Succeeded,
}
