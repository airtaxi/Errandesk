using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Errandesk.Server.Notifications;

/// <summary>
/// Sends notifications to Android devices through the Firebase Cloud Messaging HTTP v1 API, authenticated with the configured service account.
/// </summary>
public sealed class FcmPushNotificationSender(FcmSettings settings, ILogger<FcmPushNotificationSender> logger, Google.Apis.Http.HttpClientFactory? httpClientFactory = null) : IPushNotificationSender, IDisposable
{
    /// <summary>Android notification channel that shows reminders.</summary>
    public const string ReminderChannelId = "reminders";

    private readonly FirebaseApp _application = CreateApplication(settings, httpClientFactory);

    public string Platform => DevicePlatforms.Android;

    public async Task<PushSendResult> SendAsync(string pushToken, PushNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await FirebaseMessaging.GetMessaging(_application).SendAsync(CreateMessage(pushToken, notification), cancellationToken);
            return PushSendResult.Delivered;
        }
        catch (FirebaseMessagingException exception) when (exception.MessagingErrorCode == MessagingErrorCode.Unregistered) { return PushSendResult.InvalidToken; }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Sending a notification through FCM failed.");
            return PushSendResult.Failed;
        }
    }

    public void Dispose() => _application.Delete();

    /// <summary>A high-priority message for one registration token with the notification on the reminders channel and the data as message data.</summary>
    public static Message CreateMessage(string pushToken, PushNotification notification)
    {
#pragma warning disable CS0618 // Android clients register FCM registration tokens, which messages address through Token.
        return new Message
        {
            Token = pushToken,
            Notification = new Notification { Title = notification.Title, Body = notification.Body },
            Data = notification.Data,
            Android = new AndroidConfig { Priority = Priority.High, Notification = new AndroidNotification { ChannelId = ReminderChannelId } },
        };
#pragma warning restore CS0618
    }

    /// <summary>Creates a named Firebase app whose credential is built from the service account's email and private key.</summary>
    private static FirebaseApp CreateApplication(FcmSettings settings, Google.Apis.Http.HttpClientFactory? httpClientFactory)
    {
        var credentialInitializer = new ServiceAccountCredential.Initializer(settings.ClientEmail) { ProjectId = settings.ProjectId };
        if (httpClientFactory is not null) credentialInitializer.HttpClientFactory = httpClientFactory;
        var credential = new ServiceAccountCredential(credentialInitializer.FromPrivateKey(settings.PrivateKey));
        var options = new AppOptions { Credential = GoogleCredential.FromServiceAccountCredential(credential), ProjectId = settings.ProjectId };
        if (httpClientFactory is not null) options.HttpClientFactory = httpClientFactory;
        return FirebaseApp.Create(options, $"errandesk-{Guid.CreateVersion7():N}");
    }
}
