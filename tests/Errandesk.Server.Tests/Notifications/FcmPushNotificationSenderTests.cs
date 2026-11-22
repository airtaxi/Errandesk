using System.Net;
using System.Text.Json;
using Errandesk.Server.Configuration;
using Errandesk.Server.Notifications;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Errandesk.Server.Tests.Notifications;

public sealed class FcmPushNotificationSenderTests
{
    private const string ProjectId = "errandesk-test";
    private const string AccessToken = "test-access-token";

    private static readonly PushNotification s_notification = ReminderNotificationText.Create("Buy milk", "ko", Guid.Parse("0195d2a0-0000-7000-8000-000000000001"), Guid.Parse("0195d2a0-0000-7000-8000-000000000002"));

    [Fact]
    public async Task Send_AuthenticatesWithTheServiceAccountAndPostsAnHttpV1Message()
    {
        var handler = CreateFcmHandler(_ => RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.OK, $"{{\"name\":\"projects/{ProjectId}/messages/1\"}}"));
        using var sender = CreateSender(handler);

        var result = await sender.SendAsync("registration-token", s_notification, TestContext.Current.CancellationToken);

        Assert.Equal(PushSendResult.Delivered, result);
        var tokenRequest = Assert.Single(handler.Requests, request => request.Uri.Host == "oauth2.googleapis.com");
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer", tokenRequest.Body);
        var sendRequest = Assert.Single(handler.Requests, request => request.Uri.Host == "fcm.googleapis.com");
        Assert.Equal(HttpMethod.Post, sendRequest.Method);
        Assert.Equal($"/v1/projects/{ProjectId}/messages:send", sendRequest.Uri.AbsolutePath);
        Assert.Equal($"Bearer {AccessToken}", sendRequest.Headers["Authorization"]);

        using var body = JsonDocument.Parse(sendRequest.Body);
        var message = body.RootElement.GetProperty("message");
        Assert.Equal("registration-token", message.GetProperty("token").GetString());
        Assert.Equal("Buy milk", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("할 일을 확인할 시간입니다.", message.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("0195d2a0-0000-7000-8000-000000000001", message.GetProperty("data").GetProperty("taskId").GetString());
        Assert.Equal("0195d2a0-0000-7000-8000-000000000002", message.GetProperty("data").GetProperty("listId").GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
        Assert.Equal(FcmPushNotificationSender.ReminderChannelId, message.GetProperty("android").GetProperty("notification").GetProperty("channel_id").GetString());
    }

    [Fact]
    public async Task Send_ReportsUnregisteredTokensAsInvalid()
    {
        const string unregisteredError = """{"error":{"code":404,"message":"Requested entity was not found.","status":"NOT_FOUND","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""";
        var handler = CreateFcmHandler(_ => RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.NotFound, unregisteredError));
        using var sender = CreateSender(handler);

        Assert.Equal(PushSendResult.InvalidToken, await sender.SendAsync("stale-token", s_notification, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Send_ReportsOtherErrorsAsFailures()
    {
        const string invalidArgumentError = """{"error":{"code":400,"message":"The registration token is not a valid FCM registration token","status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"INVALID_ARGUMENT"}]}}""";
        var handler = CreateFcmHandler(_ => RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.BadRequest, invalidArgumentError));
        using var sender = CreateSender(handler);

        Assert.Equal(PushSendResult.Failed, await sender.SendAsync("malformed-token", s_notification, TestContext.Current.CancellationToken));
    }

    private static RecordingHttpMessageHandler CreateFcmHandler(Func<RecordedHttpRequest, HttpResponseMessage> respondToSend) => new(request => request.Uri.Host == "fcm.googleapis.com" ? respondToSend(request) : RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.OK, $"{{\"access_token\":\"{AccessToken}\",\"expires_in\":3600,\"token_type\":\"Bearer\"}}"));

    private static FcmPushNotificationSender CreateSender(HttpMessageHandler handler)
    {
        var settings = new FcmSettings { ProjectId = ProjectId, ClientEmail = $"push@{ProjectId}.iam.gserviceaccount.com", PrivateKey = TestKeys.CreateRsaPrivateKeyPem() };
        return new FcmPushNotificationSender(settings, NullLogger<FcmPushNotificationSender>.Instance, new HandlerGoogleHttpClientFactory(handler));
    }
}
