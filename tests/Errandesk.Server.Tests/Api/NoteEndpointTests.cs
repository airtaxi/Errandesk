using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class NoteEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string NotesPath = "/api/notes";
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Create_NormalizesValuesAndIsIdempotent()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var noteId = Guid.CreateVersion7();

        var created = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("  Trip plan  ", "# Busan\n\n- Book *train*", noteId, ["#Travel", "travel", " Family "], IsPinned: true), HttpStatusCode.Created);
        var repeated = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Other", Id: noteId), HttpStatusCode.OK);
        var empty = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest(), HttpStatusCode.Created);

        Assert.Equal(noteId, created.Id);
        Assert.Equal("Trip plan", created.Title);
        Assert.Equal("# Busan\n\n- Book *train*", created.Content);
        Assert.Equal(["travel", "family"], created.Tags);
        Assert.Empty(created.LinkedTaskIds);
        Assert.True(created.IsPinned);
        Assert.Equal(ApiTestServer.DefaultStartTime, created.CreatedAt);
        JsonAssert.Equal(created, repeated);
        JsonAssert.Equal(created, await alice.Client.GetAsync<NoteResponse>($"{NotesPath}/{noteId}"));
        Assert.Equal("", empty.Title);
        Assert.Equal("", empty.Content);
        Assert.Empty(empty.Tags);
        Assert.True(empty.ChangeVersion > created.ChangeVersion);

        await alice.Client.DeleteAsync($"{NotesPath}/{noteId}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest("Again", Id: noteId), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithInvalidValues_ReturnsBadRequestOrNotFound()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Tags: ["two words"]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Tags: ["a,b"]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Tags: ["#"]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Tags: [new string('t', 51)]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Tags: [.. Enumerable.Range(0, 31).Select(index => $"tag{index}")]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(new string('t', 501)), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest(Id: Guid.Empty), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, NotesPath, new CreateNoteRequest("Linked", LinkedTaskIds: [Guid.CreateVersion7()]), HttpStatusCode.NotFound);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>(NotesPath));
    }

    [Fact]
    public async Task Patch_ChangesOnlySpecifiedFieldsAndNullClears()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Ideas", "Draft", Tags: ["work"]), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(5), alice);

        var pinned = await alice.Client.PatchAsync<NoteResponse>($"{NotesPath}/{note.Id}", new UpdateNoteRequest { IsPinned = true, Tags = new Optional<IReadOnlyList<string>?>(["Work", "#Later"]) });

        Assert.True(pinned.IsPinned);
        Assert.Equal(["work", "later"], pinned.Tags);
        Assert.Equal("Ideas", pinned.Title);
        Assert.Equal("Draft", pinned.Content);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddMinutes(5), pinned.UpdatedAt);
        Assert.Equal(note.CreatedAt, pinned.CreatedAt);
        Assert.True(pinned.ChangeVersion > note.ChangeVersion);

        var cleared = await alice.Client.PatchAsync<NoteResponse>($"{NotesPath}/{note.Id}", new UpdateNoteRequest { Title = null, Content = null, Tags = new Optional<IReadOnlyList<string>?>(null) });

        Assert.Equal("", cleared.Title);
        Assert.Equal("", cleared.Content);
        Assert.Empty(cleared.Tags);
        Assert.True(cleared.IsPinned);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{NotesPath}/{note.Id}", new UpdateNoteRequest { Tags = new Optional<IReadOnlyList<string>?>(["bad tag"]) }, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{NotesPath}/{Guid.CreateVersion7()}", new UpdateNoteRequest { IsPinned = false }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_WithStaleExpectedChangeVersion_ReturnsConflictWithTheCurrentNote()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plan trip"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Trip", LinkedTaskIds: [task.Id]), HttpStatusCode.Created);
        var updated = await alice.Client.PatchAsync<NoteResponse>($"{NotesPath}/{note.Id}", new UpdateNoteRequest { Content = "Busan", ExpectedChangeVersion = note.ChangeVersion });

        var conflict = ApiClient.DeserializeJson<NoteResponse>(await alice.Client.SendExpectingAsync(HttpMethod.Patch, $"{NotesPath}/{note.Id}", new UpdateNoteRequest { Content = "Jeju", ExpectedChangeVersion = note.ChangeVersion }, HttpStatusCode.Conflict));

        JsonAssert.Equal(updated, conflict);
        Assert.Equal([task.Id], conflict.LinkedTaskIds);
        Assert.Equal("Busan", (await alice.Client.GetAsync<NoteResponse>($"{NotesPath}/{note.Id}")).Content);
    }

    [Fact]
    public async Task Query_ListsPinnedNotesFirstThenTheMostRecentlyChanged()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var oldest = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Oldest"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var pinned = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Pinned", IsPinned: true), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var middle = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Middle"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        await alice.Client.PatchAsync<NoteResponse>($"{NotesPath}/{oldest.Id}", new UpdateNoteRequest { Content = "Touched" });

        Assert.Equal([pinned.Id, oldest.Id, middle.Id], (await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>(NotesPath)).Select(note => note.Id));
    }

    [Fact]
    public async Task Query_SearchesTitleContentAndTags()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var groceries = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Groceries", "Milk, eggs and bread"), HttpStatusCode.Created);
        var recipe = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Pancakes", "Mix MILK with flour", Tags: ["recipes"]), HttpStatusCode.Created);
        var meeting = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("주간 회의록", "다음 회의록을 정리하기"), HttpStatusCode.Created);
        var tagged = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Untitled thoughts", Tags: ["c#"]), HttpStatusCode.Created);

        Assert.Equal([groceries.Id], (await SearchAsync(alice, "groceries")).Select(note => note.Id));
        Assert.Equal(new[] { groceries.Id, recipe.Id }.Order(), (await SearchAsync(alice, "milk")).Select(note => note.Id).Order());
        Assert.Equal([recipe.Id], (await SearchAsync(alice, "recipe")).Select(note => note.Id));
        Assert.Equal([recipe.Id], (await SearchAsync(alice, "pancake flour")).Select(note => note.Id));
        Assert.Equal([meeting.Id], (await SearchAsync(alice, "회의록")).Select(note => note.Id));
        Assert.Equal([tagged.Id], (await SearchAsync(alice, "C#")).Select(note => note.Id));
        Assert.Equal(new[] { groceries.Id, recipe.Id }.Order(), (await SearchAsync(alice, "groceries pancakes")).Select(note => note.Id).Order());
        Assert.Empty(await SearchAsync(alice, "spinach"));
        Assert.Equal(4, (await SearchAsync(alice, "   ")).Count);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{NotesPath}?query={new string('q', 201)}", null, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Query_RanksTitleMatchesFirstAmongUnpinnedNotes()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var contentMatch = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Weekend", "Visit the garden center"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var titleMatch = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Garden", "Water the roses"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var pinnedContentMatch = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Chores", "Mow the garden", IsPinned: true), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        await alice.Client.PatchAsync<NoteResponse>($"{NotesPath}/{contentMatch.Id}", new UpdateNoteRequest { Title = "Saturday" });

        Assert.Equal([pinnedContentMatch.Id, titleMatch.Id, contentMatch.Id], (await SearchAsync(alice, "garden")).Select(note => note.Id));
    }

    [Fact]
    public async Task Query_FiltersByTagAndLinkedTask()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Prepare talk"), HttpStatusCode.Created);
        var otherTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Unrelated"), HttpStatusCode.Created);
        var slides = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Slides", "Outline", Tags: ["work", "talk"], LinkedTaskIds: [task.Id]), HttpStatusCode.Created);
        var speech = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Speech", "Opening joke", Tags: ["work"]), HttpStatusCode.Created);
        await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Holiday", "Outline of the trip", Tags: ["home"]), HttpStatusCode.Created);

        Assert.Equal(new[] { slides.Id, speech.Id }.Order(), (await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?tag=work")).Select(note => note.Id).Order());
        Assert.Equal([slides.Id], (await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?tag=%23Talk")).Select(note => note.Id));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?tag=unused"));
        Assert.Equal([slides.Id], (await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?linkedTaskId={task.Id}")).Select(note => note.Id));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?linkedTaskId={otherTask.Id}"));
        Assert.Equal([slides.Id], (await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?query=outline&tag=work")).Select(note => note.Id));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?query=joke&linkedTaskId={task.Id}"));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{NotesPath}?tag=two%20words", null, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{NotesPath}?linkedTaskId={Guid.CreateVersion7()}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tags_CountNotesPerTagIgnoringDeletedNotes()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("One", Tags: ["work", "ideas"]), HttpStatusCode.Created);
        await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Two", Tags: ["Work"]), HttpStatusCode.Created);
        var deleted = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Three", Tags: ["work", "archive"]), HttpStatusCode.Created);
        await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Untagged"), HttpStatusCode.Created);

        Assert.Equal([new TagResponse("archive", 1), new TagResponse("ideas", 1), new TagResponse("work", 3)], await alice.Client.GetAsync<IReadOnlyList<TagResponse>>("/api/tags"));

        await alice.Client.DeleteAsync($"{NotesPath}/{deleted.Id}");

        Assert.Equal([new TagResponse("ideas", 1), new TagResponse("work", 2)], await alice.Client.GetAsync<IReadOnlyList<TagResponse>>("/api/tags"));
    }

    [Fact]
    public async Task LinkAndUnlink_ChangeTheNoteAndTheTasksLinkedNoteSummaries()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Renovate kitchen"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Kitchen", "## Budget\n\n   Tiles   and\npaint", Tags: ["home"]), HttpStatusCode.Created);
        var linkPath = $"{NotesPath}/{note.Id}/linked-tasks/{task.Id}";

        Assert.Empty(task.LinkedNotes);

        var linked = await alice.Client.SendAsync<NoteResponse>(HttpMethod.Put, linkPath);
        var linkedAgain = await alice.Client.SendAsync<NoteResponse>(HttpMethod.Put, linkPath);

        Assert.Equal([task.Id], linked.LinkedTaskIds);
        Assert.True(linked.ChangeVersion > note.ChangeVersion);
        JsonAssert.Equal(linked, linkedAgain);

        var taskWithNote = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        var summary = Assert.Single(taskWithNote.LinkedNotes);
        Assert.Equal(new NoteSummaryResponse(note.Id, "Kitchen", "## Budget Tiles and paint", summary.Tags, false, linked.UpdatedAt), summary);
        Assert.Equal(["home"], summary.Tags);
        Assert.Equal(task.ChangeVersion, taskWithNote.ChangeVersion);
        Assert.Single(Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath)).LinkedNotes);
        Assert.Single((await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsImportant = true })).LinkedNotes);
        Assert.Single((await alice.Client.PostAsync<TaskItemResponse>($"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Measure"))).LinkedNotes);

        var unlinked = await alice.Client.SendAsync<NoteResponse>(HttpMethod.Delete, linkPath);
        var unlinkedAgain = await alice.Client.SendAsync<NoteResponse>(HttpMethod.Delete, linkPath);

        Assert.Empty(unlinked.LinkedTaskIds);
        Assert.True(unlinked.ChangeVersion > linked.ChangeVersion);
        JsonAssert.Equal(unlinked, unlinkedAgain);
        Assert.Empty((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).LinkedNotes);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Put, $"{NotesPath}/{note.Id}/linked-tasks/{Guid.CreateVersion7()}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Put, $"{NotesPath}/{Guid.CreateVersion7()}/linked-tasks/{task.Id}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LinkedNotes_AreOrderedPinnedFirstThenMostRecentlyChangedPerTask()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var firstTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("First"), HttpStatusCode.Created);
        var secondTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Second"), HttpStatusCode.Created);
        var older = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Older", LinkedTaskIds: [firstTask.Id, secondTask.Id]), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var pinned = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Pinned", IsPinned: true, LinkedTaskIds: [firstTask.Id]), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(1), alice);
        var newer = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Newer", LinkedTaskIds: [firstTask.Id]), HttpStatusCode.Created);

        var tasks = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath);

        Assert.Equal([pinned.Id, newer.Id, older.Id], tasks.Single(task => task.Id == firstTask.Id).LinkedNotes.Select(summary => summary.Id));
        Assert.Equal([older.Id], tasks.Single(task => task.Id == secondTask.Id).LinkedNotes.Select(summary => summary.Id));
        Assert.Equal([firstTask.Id, secondTask.Id], older.LinkedTaskIds);
    }

    [Fact]
    public async Task DeletedTasks_DisappearFromLinksAndDeletedNotesFromTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var keptTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Kept"), HttpStatusCode.Created);
        var deletedTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Deleted"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Shared notes", LinkedTaskIds: [keptTask.Id, deletedTask.Id]), HttpStatusCode.Created);
        var otherNote = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Soon deleted", LinkedTaskIds: [keptTask.Id]), HttpStatusCode.Created);

        await alice.Client.DeleteAsync($"{TasksPath}/{deletedTask.Id}");

        var reloaded = await alice.Client.GetAsync<NoteResponse>($"{NotesPath}/{note.Id}");
        Assert.Equal([keptTask.Id], reloaded.LinkedTaskIds);
        Assert.Equal(note.ChangeVersion, reloaded.ChangeVersion);
        Assert.Equal(new[] { deletedTask.Id, keptTask.Id }.Order(), (await server.FindRawAsync(server.Database.Notes, note.Id)).LinkedTaskIds.Order());
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{NotesPath}?linkedTaskId={deletedTask.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Put, $"{NotesPath}/{note.Id}/linked-tasks/{deletedTask.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal([keptTask.Id], (await alice.Client.SendAsync<NoteResponse>(HttpMethod.Delete, $"{NotesPath}/{note.Id}/linked-tasks/{deletedTask.Id}")).LinkedTaskIds);

        await alice.Client.DeleteAsync($"{NotesPath}/{otherNote.Id}");

        Assert.Equal([note.Id], (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{keptTask.Id}")).LinkedNotes.Select(summary => summary.Id));
    }

    [Fact]
    public async Task Delete_KeepsATombstoneAndHidesTheNote()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Temporary", "Searchable words", Tags: ["temp"]), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(3), alice);
        var notePath = $"{NotesPath}/{note.Id}";

        await alice.Client.DeleteAsync(notePath);

        var tombstone = await server.FindRawAsync(server.Database.Notes, note.Id);
        Assert.True(tombstone.IsDeleted);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddMinutes(3).UtcDateTime, tombstone.DeletedAt);
        Assert.True(tombstone.ChangeVersion > note.ChangeVersion);
        Assert.Equal("Temporary", tombstone.Title);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, notePath, null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, notePath, new UpdateNoteRequest { Title = "Back" }, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, notePath, null, HttpStatusCode.NotFound);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<NoteResponse>>(NotesPath));
        Assert.Empty(await SearchAsync(alice, "searchable"));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<TagResponse>>("/api/tags"));
    }

    private static Task<IReadOnlyList<NoteResponse>> SearchAsync(TestUser user, string query) => user.Client.GetAsync<IReadOnlyList<NoteResponse>>($"{NotesPath}?query={Uri.EscapeDataString(query)}");
}
