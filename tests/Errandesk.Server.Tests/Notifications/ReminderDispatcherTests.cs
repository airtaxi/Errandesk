using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Notifications;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Notifications;

public sealed class ReminderDispatcherTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    private readonly FakePushNotificationSender _androidSender = new(DevicePlatforms.Android);
    private readonly FakePushNotificationSender _iosSender = new(DevicePlatforms.Ios);

    private static DateTimeOffset Now => ApiTestServer.DefaultStartTime;

    [Fact]
    public async Task Dispatch_SendsOnlyDueRemindersToTheMobileDevicesOfTheUser()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "alice-android");
        await RegisterDeviceAsync(alice, DevicePlatforms.Ios, "abcdef");
        await RegisterDeviceAsync(alice, DevicePlatforms.Windows, null);
        await RegisterDeviceAsync(bob, DevicePlatforms.Android, "bob-android");
        var due = await CreateTaskAsync(alice, "Call the dentist", Now.AddMinutes(-1));
        var later = await CreateTaskAsync(alice, "Buy flowers", Now.AddMinutes(10));
        var completed = await CreateTaskAsync(alice, "Done already", Now.AddMinutes(-5));
        await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{completed.Id}", new UpdateTaskRequest { IsCompleted = true });
        var deleted = await CreateTaskAsync(alice, "Deleted", Now.AddMinutes(-5));
        await alice.Client.DeleteAsync($"{TasksPath}/{deleted.Id}");
        var dispatcher = server.Services.GetRequiredService<ReminderDispatcher>();

        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));

        var androidDelivery = Assert.Single(_androidSender.Sent);
        Assert.Equal("alice-android", androidDelivery.PushToken);
        Assert.Equal("Call the dentist", androidDelivery.Notification.Title);
        Assert.Equal("It's time for this task.", androidDelivery.Notification.Body);
        Assert.Equal(due.Id.ToString(), androidDelivery.Notification.Data[ReminderNotificationText.TaskIdKey]);
        Assert.Equal(due.ListId.ToString(), androidDelivery.Notification.Data[ReminderNotificationText.ListIdKey]);
        Assert.Equal("abcdef", Assert.Single(_iosSender.Sent).PushToken);
        var stored = await server.FindRawAsync(server.Database.Tasks, due.Id);
        Assert.Equal(Now.UtcDateTime, stored.ReminderSentAt);
        Assert.Null(stored.ReminderClaimedAt);
        Assert.Null(stored.ReminderClaimId);
        Assert.True(stored.ChangeVersion > due.ChangeVersion);

        server.TimeProvider.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["Call the dentist", "Buy flowers"], _androidSender.Sent.Select(delivery => delivery.Notification.Title));
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, later.Id)).ReminderSentAt);
        Assert.Null((await server.FindRawAsync(server.Database.Tasks, completed.Id)).ReminderSentAt);
    }

    [Fact]
    public async Task Dispatch_UsesTheLanguageOfTheUser()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice", language: UserLanguages.Korean);
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        await CreateTaskAsync(alice, "우유 사기", Now);

        await server.Services.GetRequiredService<ReminderDispatcher>().DispatchDueRemindersAsync(TestContext.Current.CancellationToken);

        var delivery = Assert.Single(_androidSender.Sent);
        Assert.Equal("우유 사기", delivery.Notification.Title);
        Assert.Equal("할 일을 확인할 시간입니다.", delivery.Notification.Body);
    }

    [Fact]
    public async Task ConcurrentDispatchers_SendEveryReminderExactlyOnce()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        List<Guid> taskIds = [];
        for (var index = 0; index < 25; index++) taskIds.Add((await CreateTaskAsync(alice, $"Reminder {index}", Now.AddMinutes(-index))).Id);
        var dispatcher = server.Services.GetRequiredService<ReminderDispatcher>();

        var handledCounts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.Equal(25, handledCounts.Sum());
        Assert.Equal(25, _androidSender.Sent.Count);
        Assert.Equal(taskIds.Order(), _androidSender.Sent.Select(delivery => Guid.Parse(delivery.Notification.Data[ReminderNotificationText.TaskIdKey])).Order());
        Assert.Equal(0, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispatch_TakesOverAbandonedClaimsOnly()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        var task = await CreateTaskAsync(alice, "Claimed elsewhere", Now.AddMinutes(-1));
        var update = Builders<TaskDocument>.Update.Set(stored => stored.ReminderClaimedAt, Now.UtcDateTime.AddSeconds(-30)).Set(stored => stored.ReminderClaimId, Guid.CreateVersion7());
        await server.Database.Tasks.UpdateOneAsync(stored => stored.Id == task.Id, update, cancellationToken: TestContext.Current.CancellationToken);
        var dispatcher = server.Services.GetRequiredService<ReminderDispatcher>();

        Assert.Equal(0, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        server.TimeProvider.Advance(TimeSpan.FromSeconds(80));
        Assert.Equal(0, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        server.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));

        Assert.Single(_androidSender.Sent);
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, task.Id)).ReminderSentAt);
    }

    [Fact]
    public async Task Dispatch_RemovesDevicesWithRejectedTokens()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        var staleAndroid = await RegisterDeviceAsync(alice, DevicePlatforms.Android, "stale-token");
        var validAndroid = await RegisterDeviceAsync(alice, DevicePlatforms.Android, "valid-token");
        var staleIphone = await RegisterDeviceAsync(alice, DevicePlatforms.Ios, "0bad");
        _androidSender.GetResult = token => token == "stale-token" ? PushSendResult.InvalidToken : PushSendResult.Delivered;
        _iosSender.GetResult = _ => PushSendResult.InvalidToken;
        var task = await CreateTaskAsync(alice, "Check mail", Now);

        await server.Services.GetRequiredService<ReminderDispatcher>().DispatchDueRemindersAsync(TestContext.Current.CancellationToken);

        var remainingDevices = await alice.Client.GetAsync<IReadOnlyList<DeviceResponse>>("/api/devices");
        Assert.Equal([validAndroid.Id], remainingDevices.Select(device => device.Id));
        Assert.DoesNotContain(remainingDevices, device => device.Id == staleAndroid.Id || device.Id == staleIphone.Id);
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, task.Id)).ReminderSentAt);
    }

    [Fact]
    public async Task Dispatch_RetriesAfterTheClaimTimeoutWhenNothingWasDelivered()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        _androidSender.GetResult = _ => PushSendResult.Failed;
        var task = await CreateTaskAsync(alice, "Flaky", Now);
        var dispatcher = server.Services.GetRequiredService<ReminderDispatcher>();

        await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken);
        var afterFailure = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.Null(afterFailure.ReminderSentAt);
        Assert.NotNull(afterFailure.ReminderClaimId);
        Assert.Equal(0, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));

        _androidSender.GetResult = _ => PushSendResult.Delivered;
        server.TimeProvider.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, _androidSender.Sent.Count);
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, task.Id)).ReminderSentAt);
    }

    [Fact]
    public async Task Dispatch_SkipsRemindersThatAreMoreThanADayLateAndSendsChangedRemindersAgain()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        var stale = await CreateTaskAsync(alice, "Long ago", Now.AddHours(-25));
        var dispatcher = server.Services.GetRequiredService<ReminderDispatcher>();

        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_androidSender.Sent);
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, stale.Id)).ReminderSentAt);

        await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{stale.Id}", new UpdateTaskRequest { ReminderAt = Now.AddMinutes(5) });
        server.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await dispatcher.DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Long ago", Assert.Single(_androidSender.Sent).Notification.Title);
    }

    [Fact]
    public async Task Dispatch_DoesNotMarkAReminderChangedWhileItWasBeingSent()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        await RegisterDeviceAsync(alice, DevicePlatforms.Android, "token");
        var task = await CreateTaskAsync(alice, "Moving target", Now);
        var newReminderAt = Now.AddHours(1);
        _androidSender.WhileSending = () => alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { ReminderAt = newReminderAt });

        await server.Services.GetRequiredService<ReminderDispatcher>().DispatchDueRemindersAsync(TestContext.Current.CancellationToken);

        var stored = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.Equal(newReminderAt.UtcDateTime, stored.ReminderAt);
        Assert.Null(stored.ReminderSentAt);
        Assert.Null(stored.ReminderClaimId);
    }

    private Task<ApiTestServer> StartServerAsync() => ApiTestServer.StartAsync(mongoDb, configureServices: services =>
    {
        services.AddSingleton(new ReminderDispatcherOptions { PollInterval = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IPushNotificationSender>(_androidSender);
        services.AddSingleton<IPushNotificationSender>(_iosSender);
    });

    private static Task<DeviceResponse> RegisterDeviceAsync(TestUser user, string platform, string? pushToken) => user.Client.PostAsync<DeviceResponse>("/api/devices", new RegisterDeviceRequest(Guid.CreateVersion7(), platform, $"{platform} device", pushToken), HttpStatusCode.Created);

    private static Task<TaskItemResponse> CreateTaskAsync(TestUser user, string title, DateTimeOffset reminderAt) => user.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest(title, ReminderAt: reminderAt), HttpStatusCode.Created);
}
