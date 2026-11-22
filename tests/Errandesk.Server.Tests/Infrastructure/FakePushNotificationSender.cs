using System.Collections.Concurrent;
using Errandesk.Server.Notifications;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Records the notifications it is asked to send and answers with a configurable result per token.
/// </summary>
public sealed class FakePushNotificationSender(string platform) : IPushNotificationSender
{
    private readonly ConcurrentQueue<(string PushToken, PushNotification Notification)> _sent = new();

    public string Platform => platform;

    /// <summary>Decides the result for a push token. Delivers everything by default.</summary>
    public Func<string, PushSendResult> GetResult { get; set; } = _ => PushSendResult.Delivered;

    /// <summary>Runs while a notification is being sent, before the result is returned.</summary>
    public Func<Task>? WhileSending { get; set; }

    public IReadOnlyList<(string PushToken, PushNotification Notification)> Sent =>
    [.. _sent];

    public async Task<PushSendResult> SendAsync(string pushToken, PushNotification notification, CancellationToken cancellationToken)
    {
        _sent.Enqueue((pushToken, notification));
        if (WhileSending is not null) await WhileSending();
        return GetResult(pushToken);
    }
}
