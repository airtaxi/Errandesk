using System.Diagnostics;
using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class TombstonePurgeTests(MongoDbContainerFixture mongoDb)
{
    private const string ChangesPath = "/api/changes";
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Purge_RemovesOnlyTombstonesOlderThanTheRetentionPeriod()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var fixture = await CreateDeletedItemsAsync(server, alice);
        var purger = server.Services.GetRequiredService<TombstonePurger>();

        await server.AdvanceTimeAsync(TimeSpan.FromDays(20), alice);
        var recentTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Recently deleted"), HttpStatusCode.Created);
        await alice.Client.DeleteAsync($"{TasksPath}/{recentTask.Id}");
        await server.AdvanceTimeAsync(TimeSpan.FromDays(9), alice);
        Assert.Equal(0L, await purger.PurgeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await purger.GetPurgedThroughVersionAsync(TestContext.Current.CancellationToken));

        await server.AdvanceTimeAsync(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1), alice);
        Assert.Equal(4L, await purger.PurgeAsync(TestContext.Current.CancellationToken));

        Assert.False(await ExistsAsync(server.Database.TaskLists, fixture.DeletedList.Id));
        Assert.False(await ExistsAsync(server.Database.Tasks, fixture.TaskInDeletedList.Id));
        Assert.False(await ExistsAsync(server.Database.Tasks, fixture.DeletedTask.Id));
        Assert.False(await ExistsAsync(server.Database.Notes, fixture.DeletedNote.Id));
        Assert.True(await ExistsAsync(server.Database.Tasks, fixture.LiveTask.Id));
        Assert.True(await ExistsAsync(server.Database.Notes, fixture.LiveNote.Id));
        Assert.True((await server.FindRawAsync(server.Database.Tasks, recentTask.Id)).IsDeleted);
        Assert.Equal(fixture.NewestDeletionVersion, await purger.GetPurgedThroughVersionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await purger.PurgeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Changes_SinceAVersionBeforeAPurgedTombstone_RequireAResetWithEveryCurrentItem()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var beforeDeletions = (await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0")).CurrentVersion;
        var fixture = await CreateDeletedItemsAsync(server, alice);
        var afterDeletions = (await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0")).CurrentVersion;

        await server.AdvanceTimeAsync(TimeSpan.FromDays(20), alice);
        await server.AdvanceTimeAsync(TimeSpan.FromDays(11), alice);
        var beforePurge = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={beforeDeletions}");
        Assert.False(beforePurge.ResetRequired);
        Assert.Contains(beforePurge.DeletedTasks, tombstone => tombstone.Id == fixture.DeletedTask.Id);

        await server.Services.GetRequiredService<TombstonePurger>().PurgeAsync(TestContext.Current.CancellationToken);
        var reset = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={beforeDeletions}");
        var upToDate = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={afterDeletions}");
        var fullSync = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0");

        Assert.True(reset.ResetRequired);
        var defaultList = await server.GetDefaultListAsync(alice);
        Assert.Equal([defaultList.Id], reset.Lists.Select(list => list.Id));
        Assert.Equal([fixture.LiveTask.Id], reset.Tasks.Select(task => task.Id));
        Assert.Equal([fixture.LiveNote.Id], reset.Notes.Select(note => note.Id));
        Assert.Empty(reset.DeletedTasks);
        Assert.True(reset.CurrentVersion >= afterDeletions);
        Assert.False(upToDate.ResetRequired);
        Assert.Empty(upToDate.Tasks);
        Assert.False(fullSync.ResetRequired);
        Assert.Equal([fixture.LiveTask.Id], fullSync.Tasks.Select(task => task.Id));
    }

    [Fact]
    public async Task ResetRequired_IsWrittenAsCamelCaseJson()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var json = await alice.Client.SendExpectingAsync(HttpMethod.Get, $"{ChangesPath}?since=0", null, HttpStatusCode.OK);

        Assert.Contains("\"resetRequired\":false", json);
    }

    [Fact]
    public async Task BackgroundPurge_RunsAfterTheInitialDelay()
    {
        var options = new TombstonePurgeOptions { InitialDelay = TimeSpan.FromMinutes(1), Interval = TimeSpan.FromHours(6) };
        await using var server = await ApiTestServer.StartAsync(mongoDb, configureServices: services => services.AddSingleton(options));
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Old task"), HttpStatusCode.Created);
        await alice.Client.DeleteAsync($"{TasksPath}/{task.Id}");

        server.TimeProvider.Advance(TimeSpan.FromDays(31));

        var stopwatch = Stopwatch.StartNew();
        while (await ExistsAsync(server.Database.Tasks, task.Id) && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            server.TimeProvider.Advance(options.Interval);
        }
        Assert.False(await ExistsAsync(server.Database.Tasks, task.Id));
    }

    private static async Task<DeletedItems> CreateDeletedItemsAsync(ApiTestServer server, TestUser user)
    {
        var deletedList = await user.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var taskInDeletedList = await user.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: deletedList.Id), HttpStatusCode.Created);
        var deletedTask = await user.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Old task"), HttpStatusCode.Created);
        var liveTask = await user.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants"), HttpStatusCode.Created);
        var deletedNote = await user.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Old note"), HttpStatusCode.Created);
        var liveNote = await user.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Ideas"), HttpStatusCode.Created);

        await user.Client.DeleteAsync($"{TasksPath}/{deletedTask.Id}");
        await user.Client.DeleteAsync($"/api/notes/{deletedNote.Id}");
        await user.Client.DeleteAsync($"/api/lists/{deletedList.Id}");

        var newestDeletionVersion = (await server.FindRawAsync(server.Database.TaskLists, deletedList.Id)).ChangeVersion;
        newestDeletionVersion = Math.Max(newestDeletionVersion, (await server.FindRawAsync(server.Database.Tasks, taskInDeletedList.Id)).ChangeVersion);
        return new DeletedItems(deletedList, taskInDeletedList, deletedTask, liveTask, deletedNote, liveNote, newestDeletionVersion);
    }

    private static Task<bool> ExistsAsync<TDocument>(IMongoCollection<TDocument> collection, Guid identifier) where TDocument : DocumentBase => collection.Find(Builders<TDocument>.Filter.Eq(document => document.Id, identifier)).AnyAsync(TestContext.Current.CancellationToken);

    private sealed record DeletedItems(TaskListResponse DeletedList, TaskItemResponse TaskInDeletedList, TaskItemResponse DeletedTask, TaskItemResponse LiveTask, NoteResponse DeletedNote, NoteResponse LiveNote, long NewestDeletionVersion);
}
