namespace Errandesk.Server.Notifications;

/// <summary>
/// Sends push notifications to the devices of one platform.
/// </summary>
public interface IPushNotificationSender
{
    /// <summary>The device platform this sender delivers to, one of <see cref="Contracts.DevicePlatforms.Mobile"/>.</summary>
    string Platform { get; }

    /// <summary>Sends the notification to one device. Failures are reported in the result instead of being thrown.</summary>
    Task<PushSendResult> SendAsync(string pushToken, PushNotification notification, CancellationToken cancellationToken);
}
