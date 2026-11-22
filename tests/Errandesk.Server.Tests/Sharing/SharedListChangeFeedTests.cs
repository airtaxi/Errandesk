using System.Net;
using System.Net.ServerSentEvents;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;
using static Errandesk.Server.Tests.Sharing.SharingTestHelpers;

namespace Errandesk.Server.Tests.Sharing;

public sealed class SharedListChangeFeedTests(MongoDbContainerFixture mongoDb)
{
    private static readonly TimeSpan s_readTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task GainingAccess_ReturnsTheListAndEveryTaskInIt()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Groceries");
        var milk = await CreateTaskAsync(alice, list.Id, "Buy milk");
        var eggs = await CreateTaskAsync(alice, list.Id, "Buy eggs");
        var deleted = await CreateTaskAsync(alice, list.Id, "Old task");
        await alice.Client.DeleteAsync($"{TasksPath}/{deleted.Id}");
        var bobSince = await GetCurrentVersionAsync(bob);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob);

        var shared = await ShareAsync(alice, list.Id, "bob");
        var changes = await GetChangesAsync(bob, bobSince);

        JsonAssert.Equal(shared, Assert.Single(changes.Lists));
        Assert.Equal(new[] { milk.Id, eggs.Id }.Order(), changes.Tasks.Select(task => task.Id).Order());
        Assert.True(changes.Tasks.All(task => task.ChangeVersion < bobSince));
        Assert.Empty(changes.DeletedTasks);
        Assert.Empty(changes.DeletedLists);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob);
        var later = await GetChangesAsync(bob, changes.CurrentVersion);
        Assert.Empty(later.Lists);
        Assert.Empty(later.Tasks);

        var updated = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}", new UpdateTaskRequest { IsImportant = true });
        var afterUpdate = await GetChangesAsync(bob, later.CurrentVersion);
        JsonAssert.Equal(updated, Assert.Single(afterUpdate.Tasks));
    }

    [Fact]
    public async Task LosingAccess_ReturnsTombstonesForTheListAndItsTasksAndNothingElse()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var carol = await server.RegisterInvitedAsync(alice, "carol");
        var list = await CreateListAsync(alice, "Party");
        var cake = await CreateTaskAsync(alice, list.Id, "Order the cake");
        var music = await CreateTaskAsync(alice, list.Id, "Pick the music");
        await ShareAsync(alice, list.Id, "bob");
        await ShareAsync(alice, list.Id, "carol");
        var bobSince = await GetCurrentVersionAsync(bob);
        var carolSince = await GetCurrentVersionAsync(carol);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob, carol);

        await RemoveMemberAsync(alice, list.Id, bob.Id);
        await RemoveMemberAsync(carol, list.Id, carol.Id);

        await AssertListDroppedAsync(bob, bobSince, list.Id, [cake.Id, music.Id]);
        await AssertListDroppedAsync(carol, carolSince, list.Id, [cake.Id, music.Id]);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice, bob);
        var bobCaughtUp = (await GetChangesAsync(bob, bobSince)).CurrentVersion;
        await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{cake.Id}", new UpdateTaskRequest { Title = "Order the chocolate cake" });
        var bobLater = await GetChangesAsync(bob, bobCaughtUp);
        Assert.Empty(bobLater.Tasks);
        Assert.DoesNotContain(bobLater.Lists, changed => changed.Id == list.Id);
        Assert.DoesNotContain("chocolate", ApiClient.SerializeJson(bobLater));
    }

    private static async Task AssertListDroppedAsync(TestUser formerMember, long since, Guid listId, Guid[] taskIds)
    {
        var changes = await GetChangesAsync(formerMember, since);
        Assert.Equal(listId, Assert.Single(changes.DeletedLists).Id);
        Assert.Equal(taskIds.Order(), changes.DeletedTasks.Select(tombstone => tombstone.Id).Order());
        Assert.Empty(changes.Lists);
        Assert.Empty(changes.Tasks);

        var snapshot = await GetChangesAsync(formerMember, 0);
        Assert.DoesNotContain(listId, snapshot.Lists.Select(changed => changed.Id));
        Assert.DoesNotContain(listId, snapshot.DeletedLists.Select(tombstone => tombstone.Id));
        Assert.Empty(snapshot.Tasks);
    }

    [Fact]
    public async Task EventStreams_OfMembers_ReceiveChangesToSharedListsAndTheirRemoval()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Groceries");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_readTimeout);
        using var response = await bob.Client.OpenStreamAsync("/api/events", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var events = SseParser.Create(stream).EnumerateAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var shared = await ShareAsync(alice, list.Id, "bob");
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(ServerEventNames.Changed, events.Current.EventType);
        Assert.Equal(shared.ChangeVersion, ApiClient.DeserializeJson<ChangedEventData>(events.Current.Data).Version);

        var task = await CreateTaskAsync(alice, list.Id, "Buy milk");
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(task.ChangeVersion, ApiClient.DeserializeJson<ChangedEventData>(events.Current.Data).Version);

        await RemoveMemberAsync(alice, list.Id, bob.Id);
        Assert.True(await events.MoveNextAsync());
        var removalVersion = (await server.FindRawAsync(server.Database.TaskLists, list.Id)).ChangeVersion;
        Assert.Equal(removalVersion, ApiClient.DeserializeJson<ChangedEventData>(events.Current.Data).Version);
    }
}
