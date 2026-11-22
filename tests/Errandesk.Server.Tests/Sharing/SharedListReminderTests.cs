using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Notifications;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Errandesk.Server.Tests.Sharing.SharingTestHelpers;

namespace Errandesk.Server.Tests.Sharing;

public sealed class SharedListReminderTests(MongoDbContainerFixture mongoDb)
{
    private readonly FakePushNotificationSender _androidSender = new(DevicePlatforms.Android);

    private static DateTimeOffset Now => ApiTestServer.DefaultStartTime;

    [Fact]
    public async Task Reminders_InSharedLists_NotifyOnlyTheUserWhoSetThem()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        await RegisterDeviceAsync(alice, "alice-phone");
        await RegisterDeviceAsync(bob, "bob-phone");
        var list = await CreateListAsync(alice, "Household");
        await ShareAsync(alice, list.Id, "bob");
        var trash = await CreateTaskAsync(alice, list.Id, "Take out the trash");
        var plants = await CreateTaskAsync(alice, list.Id, "Water the plants");

        var bobsReminder = await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{trash.Id}", new UpdateTaskRequest { ReminderAt = Now.AddMinutes(-1) });
        var alicesReminder = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{plants.Id}", new UpdateTaskRequest { ReminderAt = Now.AddMinutes(-1) });
        Assert.Equal(bob.Id, bobsReminder.ReminderSetByUserId);
        Assert.Equal(alice.Id, alicesReminder.ReminderSetByUserId);
        Assert.Equal(bob.Id, (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{trash.Id}")).ReminderSetByUserId);

        Assert.Equal(2, await server.Services.GetRequiredService<ReminderDispatcher>().DispatchDueRemindersAsync(TestContext.Current.CancellationToken));

        var deliveries = _androidSender.Sent.ToDictionary(delivery => delivery.Notification.Title, delivery => delivery.PushToken);
        Assert.Equal(2, deliveries.Count);
        Assert.Equal("bob-phone", deliveries["Take out the trash"]);
        Assert.Equal("alice-phone", deliveries["Water the plants"]);
    }

    [Fact]
    public async Task Reminders_OfAUserWhoLostAccess_AreNotSent()
    {
        await using var server = await StartServerAsync();
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        await RegisterDeviceAsync(alice, "alice-phone");
        await RegisterDeviceAsync(bob, "bob-phone");
        var list = await CreateListAsync(alice, "Household");
        await ShareAsync(alice, list.Id, "bob");
        var task = await CreateTaskAsync(alice, list.Id, "Pay the bills");
        await bob.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { ReminderAt = Now.AddMinutes(5) });

        await RemoveMemberAsync(bob, list.Id, bob.Id);
        server.TimeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, await server.Services.GetRequiredService<ReminderDispatcher>().DispatchDueRemindersAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_androidSender.Sent);
        Assert.NotNull((await server.FindRawAsync(server.Database.Tasks, task.Id)).ReminderSentAt);
    }

    private Task<ApiTestServer> StartServerAsync() => ApiTestServer.StartAsync(mongoDb, configureServices: services =>
    {
        services.AddSingleton(new ReminderDispatcherOptions { PollInterval = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IPushNotificationSender>(_androidSender);
    });

    private static Task<DeviceResponse> RegisterDeviceAsync(TestUser user, string pushToken) => user.Client.PostAsync<DeviceResponse>("/api/devices", new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Android, "Phone", pushToken), HttpStatusCode.Created);
}
