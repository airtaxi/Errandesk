using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;

namespace Errandesk.Server.Notifications;

/// <summary>
/// Sends alert notifications to iOS devices through the Apple Push Notification service over HTTP/2 with token-based authentication.
/// </summary>
public sealed class ApnsPushNotificationSender(IHttpClientFactory httpClientFactory, ApnsSettings settings, ApnsProviderTokenGenerator tokenGenerator, TimeProvider timeProvider, ILogger<ApnsPushNotificationSender> logger) : IPushNotificationSender
{
    /// <summary>Name of the HTTP client used for APNs requests.</summary>
    public const string HttpClientName = "apns";

    /// <summary>How long APNs keeps trying to deliver a notification to an offline device.</summary>
    public static readonly TimeSpan DeliveryWindow = TimeSpan.FromDays(1);

    private static readonly Uri s_productionAddress = new("https://api.push.apple.com/");
    private static readonly Uri s_sandboxAddress = new("https://api.sandbox.push.apple.com/");
    private static readonly HashSet<string> s_providerTokenErrors = ["ExpiredProviderToken", "InvalidProviderToken"];

    public string Platform => DevicePlatforms.Ios;

    /// <summary>The APNs host of the environment.</summary>
    public static Uri GetServerAddress(ApnsEnvironment environment) => environment == ApnsEnvironment.Sandbox ? s_sandboxAddress : s_productionAddress;

    public async Task<PushSendResult> SendAsync(string pushToken, PushNotification notification, CancellationToken cancellationToken)
    {
        var providerToken = tokenGenerator.GetToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(GetServerAddress(settings.Environment), $"3/device/{Uri.EscapeDataString(pushToken)}"))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(CreatePayload(notification)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", providerToken);
        request.Headers.Add("apns-topic", settings.BundleId);
        request.Headers.Add("apns-push-type", "alert");
        request.Headers.Add("apns-priority", "10");
        request.Headers.Add("apns-expiration", (timeProvider.GetUtcNow() + DeliveryWindow).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return PushSendResult.Delivered;
            if (response.StatusCode == HttpStatusCode.Gone) return PushSendResult.InvalidToken;

            var reason = await ReadReasonAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden && s_providerTokenErrors.Contains(reason)) tokenGenerator.Invalidate(providerToken);
            logger.LogWarning("APNs rejected a notification with status {StatusCode} and reason {Reason}.", (int)response.StatusCode, reason);
            return PushSendResult.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "Sending a notification to APNs failed.");
            return PushSendResult.Failed;
        }
    }

    /// <summary>The JSON payload: the alert title and body with the default sound under <c>aps</c>, and the notification data as top-level string values.</summary>
    public static byte[] CreatePayload(PushNotification notification)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("aps");
            writer.WriteStartObject("alert");
            writer.WriteString("title", notification.Title);
            writer.WriteString("body", notification.Body);
            writer.WriteEndObject();
            writer.WriteString("sound", "default");
            writer.WriteEndObject();
            foreach (var item in notification.Data) writer.WriteString(item.Key, item.Value);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static async Task<string> ReadReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }
}
