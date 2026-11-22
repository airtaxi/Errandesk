using System.Net;
using System.Text.Json;
using Errandesk.Server.Configuration;
using Errandesk.Server.Notifications;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Errandesk.Server.Tests.Notifications;

public sealed class ApnsPushNotificationSenderTests
{
    private const string DeviceToken = "00fc13adff785122b4ad28809a3420982341241421348097878e577c991de8f0";

    private static readonly DateTimeOffset s_startTime = new(2031, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly PushNotification s_notification = ReminderNotificationText.Create("Call the dentist", "en", Guid.Parse("0195d2a0-0000-7000-8000-000000000001"), Guid.Parse("0195d2a0-0000-7000-8000-000000000002"));

    [Fact]
    public async Task Send_PostsAnAlertOverHttp2WithTheProviderTokenAndApnsHeaders()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var tokenGenerator = CreateTokenGenerator(out var settings, out var timeProvider);
        var sender = new ApnsPushNotificationSender(new HandlerHttpClientFactory(handler), settings, tokenGenerator, timeProvider, NullLogger<ApnsPushNotificationSender>.Instance);

        var result = await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken);

        Assert.Equal(PushSendResult.Delivered, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"https://api.push.apple.com/3/device/{DeviceToken}"), request.Uri);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        Assert.Equal($"bearer {tokenGenerator.GetToken()}", request.Headers["Authorization"]);
        Assert.Equal("com.example.errandesk", request.Headers["apns-topic"]);
        Assert.Equal("alert", request.Headers["apns-push-type"]);
        Assert.Equal("10", request.Headers["apns-priority"]);
        Assert.Equal(s_startTime.AddDays(1).ToUnixTimeSeconds().ToString(), request.Headers["apns-expiration"]);
        Assert.Equal("application/json", request.ContentType);

        using var payload = JsonDocument.Parse(request.Body);
        var alert = payload.RootElement.GetProperty("aps").GetProperty("alert");
        Assert.Equal("Call the dentist", alert.GetProperty("title").GetString());
        Assert.Equal("It's time for this task.", alert.GetProperty("body").GetString());
        Assert.Equal("default", payload.RootElement.GetProperty("aps").GetProperty("sound").GetString());
        Assert.Equal("0195d2a0-0000-7000-8000-000000000001", payload.RootElement.GetProperty("taskId").GetString());
        Assert.Equal("0195d2a0-0000-7000-8000-000000000002", payload.RootElement.GetProperty("listId").GetString());
    }

    [Fact]
    public async Task Send_UsesTheSandboxHostInTheSandboxEnvironment()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var timeProvider = new FakeTimeProvider(s_startTime);
        var settings = ApnsProviderTokenGeneratorTests.CreateSettings(TestKeys.CreateEllipticCurvePrivateKeyPem(), ApnsEnvironment.Sandbox);
        using var tokenGenerator = new ApnsProviderTokenGenerator(settings, timeProvider);
        var sender = new ApnsPushNotificationSender(new HandlerHttpClientFactory(handler), settings, tokenGenerator, timeProvider, NullLogger<ApnsPushNotificationSender>.Instance);

        await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken);

        Assert.Equal("api.sandbox.push.apple.com", Assert.Single(handler.Requests).Uri.Host);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "{\"reason\":\"Unregistered\",\"timestamp\":1937179036000}", PushSendResult.InvalidToken)]
    [InlineData(HttpStatusCode.Gone, "{\"reason\":\"ExpiredToken\"}", PushSendResult.InvalidToken)]
    [InlineData(HttpStatusCode.BadRequest, "{\"reason\":\"BadDeviceToken\"}", PushSendResult.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"reason\":\"TooManyRequests\"}", PushSendResult.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "not json", PushSendResult.Failed)]
    public async Task Send_MapsApnsResponses(HttpStatusCode statusCode, string body, PushSendResult expectedResult)
    {
        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.CreateJsonResponse(statusCode, body));
        using var tokenGenerator = CreateTokenGenerator(out var settings, out var timeProvider);
        var sender = new ApnsPushNotificationSender(new HandlerHttpClientFactory(handler), settings, tokenGenerator, timeProvider, NullLogger<ApnsPushNotificationSender>.Instance);

        Assert.Equal(expectedResult, await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Send_ReplacesTheProviderTokenAfterApnsRejectedIt()
    {
        var isFirstRequest = true;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            if (!isFirstRequest) return new HttpResponseMessage(HttpStatusCode.OK);
            isFirstRequest = false;
            return RecordingHttpMessageHandler.CreateJsonResponse(HttpStatusCode.Forbidden, "{\"reason\":\"ExpiredProviderToken\"}");
        });
        using var tokenGenerator = CreateTokenGenerator(out var settings, out var timeProvider);
        var sender = new ApnsPushNotificationSender(new HandlerHttpClientFactory(handler), settings, tokenGenerator, timeProvider, NullLogger<ApnsPushNotificationSender>.Instance);

        var firstResult = await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken);
        var secondResult = await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken);

        Assert.Equal(PushSendResult.Failed, firstResult);
        Assert.Equal(PushSendResult.Delivered, secondResult);
        Assert.NotEqual(handler.Requests[0].Headers["Authorization"], handler.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task Send_ReportsNetworkErrorsAsFailures()
    {
        var handler = new RecordingHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        using var tokenGenerator = CreateTokenGenerator(out var settings, out var timeProvider);
        var sender = new ApnsPushNotificationSender(new HandlerHttpClientFactory(handler), settings, tokenGenerator, timeProvider, NullLogger<ApnsPushNotificationSender>.Instance);

        Assert.Equal(PushSendResult.Failed, await sender.SendAsync(DeviceToken, s_notification, TestContext.Current.CancellationToken));
    }

    private static ApnsProviderTokenGenerator CreateTokenGenerator(out ApnsSettings settings, out FakeTimeProvider timeProvider)
    {
        settings = ApnsProviderTokenGeneratorTests.CreateSettings(TestKeys.CreateEllipticCurvePrivateKeyPem());
        timeProvider = new FakeTimeProvider(s_startTime);
        return new ApnsProviderTokenGenerator(settings, timeProvider);
    }
}
