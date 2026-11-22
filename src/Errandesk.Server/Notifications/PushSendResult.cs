namespace Errandesk.Server.Notifications;

/// <summary>
/// Outcome of sending a push notification to one device.
/// </summary>
public enum PushSendResult
{
    /// <summary>The push service accepted the notification.</summary>
    Delivered,

    /// <summary>The push service reported the token as no longer valid; the device should be removed.</summary>
    InvalidToken,

    /// <summary>Sending failed for another reason, such as a network error or a service outage.</summary>
    Failed,
}
