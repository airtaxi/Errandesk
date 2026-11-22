using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Notifications;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Agent;

public sealed class MorningBriefingTests(MongoDbContainerFixture mongoDb)
{
    private const string MePath = "/api/users/me";
    private const string LatestPath = "/api/briefings/latest";
    private const string ConversationsPath = "/api/conversations";
    private const string TasksPath = "/api/tasks";

    private readonly FakePushNotificationSender _androidSender = new(DevicePlatforms.Android);
    private readonly FakePushNotificationSender _iosSender = new(DevicePlatforms.Ios);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_AreDisabledByDefaultAndCanBeChangedThroughTheProfile()
    {
        await using var server = await StartServerAsync(new ScriptedChatClient(), ApiTestServer.DefaultStartTime);
        var alice = await server.RegisterAsync("alice");

        Assert.Equal(new MorningBriefingSettings(false, "07:00"), (await alice.Client.GetAsync<UserResponse>(MePath)).MorningBriefing);
        Assert.Equal(new MorningBriefingSettings(false, "07:00"), alice.Authentication.User.MorningBriefing);

        var enabled = await alice.Client.PatchAsync<UserResponse>(MePath, new UpdateUserRequest { IsMorningBriefingEnabled = true, MorningBriefingTime = " 06:45 " });
        Assert.Equal(new MorningBriefingSettings(true, "06:45"), enabled.MorningBriefing);
        Assert.Contains("\"morningBriefing\":{\"isEnabled\":true,\"time\":\"06:45\"}", await alice.Client.SendExpectingAsync(HttpMethod.Get, MePath, null, HttpStatusCode.OK), StringComparison.Ordinal);

        foreach (var invalidTime in (string[])["7:05", "24:00", "07:30:00", "", "seven"]) await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, MePath, new UpdateUserRequest { MorningBriefingTime = invalidTime }, HttpStatusCode.BadRequest);

        var renamed = await alice.Client.PatchAsync<UserResponse>(MePath, new UpdateUserRequest { DisplayName = "Alice" });
        Assert.Equal(new MorningBriefingSettings(true, "06:45"), renamed.MorningBriefing);
        var disabled = await alice.Client.PatchAsync<UserResponse>(MePath, new UpdateUserRequest { IsMorningBriefingEnabled = false });
        Assert.Equal(new MorningBriefingSettings(false, "06:45"), disabled.MorningBriefing);
        await alice.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Scheduler_WritesOneBriefingPerLocalDayInEveryTimeZone()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 9, 21, 0, 0, TimeSpan.Zero));
        var seoul = await server.RegisterAsync("seoul", timeZone: "Asia/Seoul");
        var losAngeles = await server.RegisterInvitedAsync(seoul, "losangeles", "America/Los_Angeles");
        await EnableAsync(seoul, "07:00");
        await EnableAsync(losAngeles, "07:00");
        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();

        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(65), seoul, losAngeles);
        chatClient.EnqueueText("Seoul, day one");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        var seoulFirst = await seoul.Client.GetAsync<MorningBriefingResponse>(LatestPath);
        Assert.Equal(new DateOnly(2031, 3, 10), seoulFirst.Date);
        Assert.Equal("Seoul, day one", seoulFirst.Text);
        await losAngeles.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);

        await server.AdvanceTimeAsync(TimeSpan.FromHours(16), seoul, losAngeles);
        chatClient.EnqueueText("Los Angeles, day one");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        var losAngelesFirst = await losAngeles.Client.GetAsync<MorningBriefingResponse>(LatestPath);
        Assert.Equal(new DateOnly(2031, 3, 10), losAngelesFirst.Date);
        Assert.Equal("Los Angeles, day one", losAngelesFirst.Text);
        Assert.Equal(seoulFirst, await seoul.Client.GetAsync<MorningBriefingResponse>(LatestPath));

        await server.AdvanceTimeAsync(TimeSpan.FromHours(8), seoul, losAngeles);
        chatClient.EnqueueText("Seoul, day two");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        var seoulSecond = await seoul.Client.GetAsync<MorningBriefingResponse>(LatestPath);
        Assert.Equal(new DateOnly(2031, 3, 11), seoulSecond.Date);
        Assert.Equal(seoulFirst.ConversationId, seoulSecond.ConversationId);

        Assert.Equal(3, chatClient.ReceivedMessages.Count);
        Assert.Contains("2031-03-10 (Monday)", chatClient.ReceivedMessages[0][^1].Text, StringComparison.Ordinal);
        Assert.Contains("2031-03-10T07:05", chatClient.ReceivedMessages[0][0].Text, StringComparison.Ordinal);
        Assert.Contains("2031-03-10T07:05", chatClient.ReceivedMessages[1][0].Text, StringComparison.Ordinal);
        Assert.Contains("America/Los_Angeles", chatClient.ReceivedMessages[1][0].Text, StringComparison.Ordinal);
        Assert.Contains("2031-03-11 (Tuesday)", chatClient.ReceivedMessages[2][^1].Text, StringComparison.Ordinal);

        var conversation = Assert.Single(await seoul.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath));
        Assert.True(conversation.IsBriefing);
        var messages = await seoul.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal(["Seoul, day one", "Seoul, day two"], messages.Select(message => message.Text));
        Assert.All(messages, message => Assert.Equal(ConversationRoles.Assistant, message.Role));
        Assert.Equal([seoulFirst.MessageId, seoulSecond.MessageId], messages.Select(message => message.Id));
    }

    [Fact]
    public async Task Scheduler_WaitsForTheChosenTimeAndSkipsDaysMissedByMoreThanTheLatenessWindow()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 9, 21, 0, 0, TimeSpan.Zero));
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var bob = await server.RegisterInvitedAsync(alice, "bob", "Asia/Seoul");
        await EnableAsync(alice, "08:30");
        await bob.Client.PatchAsync<UserResponse>(MePath, new UpdateUserRequest { MorningBriefingTime = "08:30" });
        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(149), alice, bob);
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(242), alice, bob);
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        await alice.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);

        await server.AdvanceTimeAsync(TimeSpan.FromHours(20) - TimeSpan.FromMinutes(1), alice, bob);
        chatClient.EnqueueText("Good morning");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Equal(new DateOnly(2031, 3, 11), (await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath)).Date);
        await bob.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);
        Assert.Single(chatClient.ReceivedMessages);
    }

    [Fact]
    public async Task ConcurrentSchedulers_WriteEveryBriefingExactlyOnce()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 10, 7, 10, 0, TimeSpan.Zero));
        var first = await server.RegisterAsync("first");
        List<TestUser> users = [first, await server.RegisterInvitedAsync(first, "second"), await server.RegisterInvitedAsync(first, "third")];
        foreach (var user in users)
        {
            await EnableAsync(user, "07:00");
            chatClient.EnqueueText("Good morning");
        }

        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();
        var writtenCounts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => scheduler.WriteDueBriefingsAsync(CancellationToken), CancellationToken)));

        Assert.Equal(3, writtenCounts.Sum());
        Assert.Equal(3, chatClient.ReceivedMessages.Count);
        foreach (var user in users) Assert.Single(await user.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{(await user.Client.GetAsync<MorningBriefingResponse>(LatestPath)).ConversationId}/messages"));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
    }

    [Fact]
    public async Task Scheduler_SkipsBriefingsWhileNoAiModelIsConfigured()
    {
        await using var server = await StartServerAsync(null, new DateTimeOffset(2031, 3, 10, 7, 10, 0, TimeSpan.Zero));
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "android-token");
        await EnableAsync(alice, "07:00");
        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();
        var stored = await server.FindRawAsync(server.Database.Users, alice.Id);

        Assert.False((await server.Anonymous.GetAsync<ServerInfoResponse>("/api/server-info")).IsAiAvailable);
        Assert.Equal(new DateOnly(2031, 3, 10), scheduler.GetDueDate(stored.MorningBriefing!, stored.TimeZone, server.TimeProvider.GetUtcNow()));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));

        stored = await server.FindRawAsync(server.Database.Users, alice.Id);
        Assert.Null(stored.MorningBriefing!.LastBriefingDate);
        Assert.Null(stored.MorningBriefing.ClaimedAt);
        Assert.Equal(0, await server.Database.Conversations.CountDocumentsAsync(FilterDefinition<ConversationDocument>.Empty, cancellationToken: CancellationToken));
        await alice.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);
        Assert.Empty(_androidSender.Sent);
    }

    [Fact]
    public async Task Briefing_SummarizesTheDayInTheBriefingConversationAndIsPushedToMobileDevices()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 10, 6, 0, 0, TimeSpan.Zero));
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("File taxes", DueDate: new DateOnly(2031, 3, 8)), HttpStatusCode.Created);
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay rent", DueDate: new DateOnly(2031, 3, 10), IsImportant: true, Steps: [new CreateTaskStepRequest("Transfer money")], ReminderAt: new DateTimeOffset(2031, 3, 10, 9, 0, 0, TimeSpan.Zero)), HttpStatusCode.Created);
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call mom", IsInMyDay: true), HttpStatusCode.Created);
        var done = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Done already", DueDate: new DateOnly(2031, 3, 10)), HttpStatusCode.Created);
        await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{done.Id}", new UpdateTaskRequest { IsCompleted = true });
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Later this month", DueDate: new DateOnly(2031, 3, 20)), HttpStatusCode.Created);
        await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Trip ideas", "Busan in **April**", Tags: ["travel"]), HttpStatusCode.Created);
        await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Bob's secret", DueDate: new DateOnly(2031, 3, 10), IsInMyDay: true), HttpStatusCode.Created);
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "alice-android");
        await RegisterDeviceAsync(alice, DevicePlatforms.Ios, "abcdef");
        await RegisterDeviceAsync(alice, DevicePlatforms.Windows, null);
        await RegisterDeviceAsync(bob, DevicePlatforms.Android, "bob-android");
        await EnableAsync(alice, "07:00");
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(61), alice, bob);

        const string BriefingText = "## Good morning\n\n- **Pay rent** is due today at 09:00.\n- `File taxes` is overdue.";
        chatClient.EnqueueText("## Good morning\n\n", "- **Pay rent** is due today at 09:00.\n- `File taxes` is overdue.");
        Assert.Equal(1, await server.Services.GetRequiredService<MorningBriefingScheduler>().WriteDueBriefingsAsync(CancellationToken));

        var request = Assert.Single(chatClient.ReceivedMessages);
        Assert.Equal([ChatRole.System, ChatRole.User], request.Select(message => message.Role));
        Assert.Contains("2031-03-10T07:01", request[0].Text, StringComparison.Ordinal);
        Assert.Contains("in English", request[1].Text, StringComparison.Ordinal);
        foreach (var expected in (string[])["File taxes", "Pay rent", "Call mom", "Trip ideas", "Busan in **April**", "travel", "2031-03-10 09:00", "0 of 1 done", "\"isImportant\":true"]) Assert.Contains(expected, request[1].Text, StringComparison.Ordinal);
        foreach (var unexpected in (string[])["Done already", "Later this month", "Bob's secret"]) Assert.DoesNotContain(unexpected, request[1].Text, StringComparison.Ordinal);
        var options = Assert.Single(chatClient.ReceivedOptions);
        Assert.Null(options!.Tools);
        Assert.Equal(1024, options.MaxOutputTokens);

        var latest = await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath);
        Assert.Equal(new DateOnly(2031, 3, 10), latest.Date);
        Assert.Equal(BriefingText, latest.Text);
        var conversation = Assert.Single(await alice.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath));
        Assert.Equal(new ConversationResponse(latest.ConversationId, MorningBriefingService.ConversationTitle, false, conversation.CreatedAt, conversation.UpdatedAt, true), conversation);
        var briefingMessage = Assert.Single(await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages"));
        Assert.Equal(latest.MessageId, briefingMessage.Id);
        Assert.Equal(BriefingText, briefingMessage.Text);
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath));

        var androidDelivery = Assert.Single(_androidSender.Sent);
        Assert.Equal("alice-android", androidDelivery.PushToken);
        Assert.Equal("Morning briefing", androidDelivery.Notification.Title);
        Assert.Equal("Good morning Pay rent is due today at 09:00. File taxes is overdue.", androidDelivery.Notification.Body);
        Assert.Equal(conversation.Id.ToString(), androidDelivery.Notification.Data[MorningBriefingNotificationText.ConversationIdKey]);
        Assert.Equal("abcdef", Assert.Single(_iosSender.Sent).PushToken);

        chatClient.EnqueueText("Start with the rent.");
        var events = await alice.Client.PostForEventsAsync($"{ConversationsPath}/{conversation.Id}/messages", new SendMessageRequest("What should I do first?"));
        Assert.Equal(ServerEventNames.MessageCompleted, events[^1].EventType);
        var followUpRequest = chatClient.ReceivedMessages[1];
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], followUpRequest.Select(message => message.Role));
        Assert.Contains("Pay rent", followUpRequest[1].Text, StringComparison.Ordinal);
        Assert.Equal(BriefingText, followUpRequest[2].Text);
        Assert.Equal("What should I do first?", followUpRequest[3].Text);
        var messages = await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal([BriefingText, "What should I do first?", "Start with the rent."], messages.Select(message => message.Text));
        Assert.Equal(latest, await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath));
    }

    [Fact]
    public async Task Briefing_IsWrittenInKoreanForKoreanUsersAndANewConversationFollowsADeletedOne()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 10, 7, 10, 0, TimeSpan.Zero));
        var alice = await server.RegisterAsync("alice", language: UserLanguages.Korean);
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "android-token");
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("우유 사기", IsInMyDay: true), HttpStatusCode.Created);
        await EnableAsync(alice, "07:00");
        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();

        chatClient.EnqueueText("좋은 아침입니다. 오늘 할 일에 우유 사기가 있습니다.");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        var request = chatClient.ReceivedMessages[0][^1].Text;
        Assert.Contains("in Korean", request, StringComparison.Ordinal);
        Assert.Contains("My Day \"오늘 할 일\"", request, StringComparison.Ordinal);
        Assert.Contains("{\"title\":\"우유 사기\",\"list\":\"작업\"", request, StringComparison.Ordinal);
        Assert.Equal("아침 브리핑", Assert.Single(_androidSender.Sent).Notification.Title);
        var first = await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath);

        await alice.Client.DeleteAsync($"{ConversationsPath}/{first.ConversationId}");
        await alice.Client.SendExpectingAsync(HttpMethod.Get, LatestPath, null, HttpStatusCode.NoContent);

        await server.AdvanceTimeAsync(TimeSpan.FromDays(1), alice);
        chatClient.EnqueueText("좋은 아침입니다.");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        var second = await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath);
        Assert.NotEqual(first.ConversationId, second.ConversationId);
        Assert.True(Assert.Single(await alice.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath)).IsBriefing);
    }

    [Fact]
    public async Task FailedBriefing_IsTriedAgainAfterTheClaimTimeout()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await StartServerAsync(chatClient, new DateTimeOffset(2031, 3, 10, 7, 5, 0, TimeSpan.Zero));
        var alice = await server.RegisterAsync("alice");
        await EnableAsync(alice, "07:00");
        var scheduler = server.Services.GetRequiredService<MorningBriefingScheduler>();

        chatClient.Enqueue(_ => throw new InvalidOperationException("The service is unavailable."));
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.NotNull((await server.FindRawAsync(server.Database.Users, alice.Id)).MorningBriefing!.ClaimId);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(9), alice);
        Assert.Equal(0, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Single(chatClient.ReceivedMessages);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(2), alice);
        chatClient.EnqueueText("Good morning");
        Assert.Equal(1, await scheduler.WriteDueBriefingsAsync(CancellationToken));
        Assert.Equal(2, chatClient.ReceivedMessages.Count);
        var stored = await server.FindRawAsync(server.Database.Users, alice.Id);
        Assert.Equal(new DateOnly(2031, 3, 10), stored.MorningBriefing!.LastBriefingDate);
        Assert.Null(stored.MorningBriefing.ClaimId);
        Assert.Equal("Good morning", (await alice.Client.GetAsync<MorningBriefingResponse>(LatestPath)).Text);
    }

    private Task<ApiTestServer> StartServerAsync(ScriptedChatClient? chatClient, DateTimeOffset startTime) => ApiTestServer.StartAsync(mongoDb, startTime: startTime, chatClient: chatClient, configureServices: services =>
    {
        services.AddSingleton(new ReminderDispatcherOptions { PollInterval = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IPushNotificationSender>(_androidSender);
        services.AddSingleton<IPushNotificationSender>(_iosSender);
    });

    private static Task<UserResponse> EnableAsync(TestUser user, string time) => user.Client.PatchAsync<UserResponse>(MePath, new UpdateUserRequest { IsMorningBriefingEnabled = true, MorningBriefingTime = time });

    private static Task<DeviceResponse> RegisterDeviceAsync(TestUser user, string platform, string? pushToken) => user.Client.PostAsync<DeviceResponse>("/api/devices", new RegisterDeviceRequest(Guid.CreateVersion7(), platform, $"{platform} device", pushToken), HttpStatusCode.Created);
}
