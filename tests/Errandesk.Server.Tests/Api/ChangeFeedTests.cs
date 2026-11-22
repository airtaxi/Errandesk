using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class ChangeFeedTests(MongoDbContainerFixture mongoDb)
{
    private const string ChangesPath = "/api/changes";
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task GetChanges_FromZero_ReturnsEveryListTaskAndNote()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Errands"), HttpStatusCode.Created);
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk", ListId: list.Id, IsInMyDay: true), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Shopping", LinkedTaskIds: [task.Id]), HttpStatusCode.Created);

        var changes = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0");

        var defaultList = await server.GetDefaultListAsync(alice);
        Assert.Equal([defaultList.Id, list.Id], changes.Lists.Select(changedList => changedList.Id));
        JsonAssert.Equal(await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}"), Assert.Single(changes.Tasks));
        Assert.True(changes.Tasks[0].IsInMyDay);
        Assert.Equal(note.Id, Assert.Single(changes.Tasks[0].LinkedNotes).Id);
        JsonAssert.Equal(note, Assert.Single(changes.Notes));
        Assert.Empty(changes.DeletedLists);
        Assert.Empty(changes.DeletedTasks);
        Assert.Empty(changes.DeletedNotes);
        Assert.True(changes.CurrentVersion >= note.ChangeVersion);
    }

    [Fact]
    public async Task GetChanges_SinceAVersion_ReturnsOnlyLaterChangesAndTombstones()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var taskInList = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: list.Id), HttpStatusCode.Created);
        var unchangedTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants"), HttpStatusCode.Created);
        var changedTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call bank"), HttpStatusCode.Created);
        var deletedTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Old task"), HttpStatusCode.Created);
        var deletedNote = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Old note"), HttpStatusCode.Created);
        var since = (await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0")).CurrentVersion;
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);

        var patched = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{changedTask.Id}", new UpdateTaskRequest { IsImportant = true });
        await alice.Client.DeleteAsync($"{TasksPath}/{deletedTask.Id}");
        await alice.Client.DeleteAsync($"/api/notes/{deletedNote.Id}");
        await alice.Client.DeleteAsync($"/api/lists/{list.Id}");
        var changes = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={since}");

        Assert.Empty(changes.Lists);
        JsonAssert.Equal(patched, Assert.Single(changes.Tasks));
        Assert.Empty(changes.Notes);
        Assert.Equal(list.Id, Assert.Single(changes.DeletedLists).Id);
        Assert.Equal([taskInList.Id, deletedTask.Id], changes.DeletedTasks.Select(tombstone => tombstone.Id).Order());
        var noteTombstone = Assert.Single(changes.DeletedNotes);
        Assert.Equal(deletedNote.Id, noteTombstone.Id);
        Assert.Equal(server.TimeProvider.GetUtcNow(), noteTombstone.DeletedAt);
        Assert.True(noteTombstone.ChangeVersion > since);
        Assert.DoesNotContain(changes.Tasks, task => task.Id == unchangedTask.Id);
        Assert.True(changes.CurrentVersion >= changes.DeletedLists[0].ChangeVersion);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var noChanges = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={changes.CurrentVersion}");
        Assert.Empty(noChanges.Tasks);
        Assert.Empty(noChanges.DeletedTasks);
        Assert.True(noChanges.CurrentVersion >= changes.CurrentVersion);
    }

    [Fact]
    public async Task GetChanges_RepeatsItemsChangedWithinTheLateCommitWindow()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var current = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0");

        var repeated = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={current.CurrentVersion}");
        await server.AdvanceTimeAsync(TimeSpan.FromSeconds(31), alice);
        var later = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since={current.CurrentVersion}");

        Assert.Equal(task.Id, Assert.Single(repeated.Tasks).Id);
        Assert.Empty(later.Tasks);
        Assert.Empty(later.Lists);
    }

    [Fact]
    public async Task GetChanges_ContainsOnlyTheUsersOwnData()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Bob's task"), HttpStatusCode.Created);
        var bobsNote = await bob.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Bob's note"), HttpStatusCode.Created);
        await bob.Client.DeleteAsync($"/api/notes/{bobsNote.Id}");

        var changes = await alice.Client.GetAsync<ChangesResponse>($"{ChangesPath}?since=0");

        Assert.True(Assert.Single(changes.Lists).IsDefault);
        Assert.Empty(changes.Tasks);
        Assert.Empty(changes.Notes);
        Assert.Empty(changes.DeletedNotes);
    }

    [Fact]
    public async Task GetChanges_WithANegativeVersionOrWithoutAToken_IsRejected()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ChangesPath}?since=-1", null, HttpStatusCode.BadRequest);
        await server.Anonymous.SendExpectingAsync(HttpMethod.Get, $"{ChangesPath}?since=0", null, HttpStatusCode.Unauthorized);
    }
}
