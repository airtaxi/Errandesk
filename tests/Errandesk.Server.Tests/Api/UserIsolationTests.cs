using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class UserIsolationTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task Users_CannotSeeOrChangeEachOthersListsAndTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var aliceList = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Private"), HttpStatusCode.Created);
        var aliceTask = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Secret", ListId: aliceList.Id, IsImportant: true, DueDate: new DateOnly(2031, 3, 20), IsInMyDay: true, Steps: [new CreateTaskStepRequest("Step")]), HttpStatusCode.Created);
        var aliceDefaultList = await server.GetDefaultListAsync(alice);
        var bobTask = await bob.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Bob's task"), HttpStatusCode.Created);

        var bobLists = await bob.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        Assert.Single(bobLists);
        Assert.NotEqual(aliceDefaultList.Id, bobLists[0].Id);
        Assert.Equal([bobTask.Id], (await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks?includeCompleted=true")).Select(task => task.Id));
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={TaskViews.Important}"));
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={TaskViews.Planned}"));
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={TaskViews.MyDay}"));

        var listPath = $"/api/lists/{aliceList.Id}";
        var taskPath = $"/api/tasks/{aliceTask.Id}";
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, listPath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, listPath, new UpdateTaskListRequest { Name = "Mine" }, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, listPath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"/api/lists/{aliceDefaultList.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks?view={aliceList.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, taskPath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, taskPath, new UpdateTaskRequest { IsCompleted = true }, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, taskPath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{taskPath}/steps", new CreateTaskStepRequest("Intrude"), HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{taskPath}/steps/{aliceTask.Steps[0].Id}", new UpdateTaskStepRequest { IsCompleted = true }, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{taskPath}/steps/{aliceTask.Steps[0].Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, "/api/tasks", new CreateTaskRequest("Into Alice's list", ListId: aliceList.Id), HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"/api/tasks/{bobTask.Id}", new UpdateTaskRequest { ListId = aliceList.Id }, HttpStatusCode.NotFound);

        JsonAssert.Equal(aliceList, await alice.Client.GetAsync<TaskListResponse>(listPath));
        JsonAssert.Equal(aliceTask, await alice.Client.GetAsync<TaskItemResponse>(taskPath));
        Assert.Equal([aliceTask.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks")).Select(task => task.Id));
    }

    [Fact]
    public async Task Users_CannotSeeOrChangeEachOthersNotesOrLinkToEachOthersTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var aliceTask = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Alice's task"), HttpStatusCode.Created);
        var aliceNote = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Diary", "Secret plans", Tags: ["private"], LinkedTaskIds: [aliceTask.Id]), HttpStatusCode.Created);
        var bobTask = await bob.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Bob's task"), HttpStatusCode.Created);
        var bobNote = await bob.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Bob's plans", "Secret plans", Tags: ["mine"], LinkedTaskIds: [bobTask.Id]), HttpStatusCode.Created);

        Assert.Equal([bobNote.Id], (await bob.Client.GetAsync<IReadOnlyList<NoteResponse>>("/api/notes")).Select(note => note.Id));
        Assert.Equal([bobNote.Id], (await bob.Client.GetAsync<IReadOnlyList<NoteResponse>>("/api/notes?query=secret")).Select(note => note.Id));
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<NoteResponse>>("/api/notes?tag=private"));
        Assert.Equal([new TagResponse("mine", 1)], await bob.Client.GetAsync<IReadOnlyList<TagResponse>>("/api/tags"));

        var notePath = $"/api/notes/{aliceNote.Id}";
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, notePath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Patch, notePath, new UpdateNoteRequest { Title = "Mine" }, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, notePath, null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Put, $"{notePath}/linked-tasks/{bobTask.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{notePath}/linked-tasks/{aliceTask.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Put, $"/api/notes/{bobNote.Id}/linked-tasks/{aliceTask.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/notes?linkedTaskId={aliceTask.Id}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, "/api/notes", new CreateNoteRequest("Intrude", LinkedTaskIds: [aliceTask.Id]), HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, "/api/notes", new CreateNoteRequest("Same ID", Id: aliceNote.Id), HttpStatusCode.Conflict);

        JsonAssert.Equal(aliceNote, await alice.Client.GetAsync<NoteResponse>(notePath));
        Assert.Equal([aliceNote.Id], (await alice.Client.GetAsync<TaskItemResponse>($"/api/tasks/{aliceTask.Id}")).LinkedNotes.Select(summary => summary.Id));
        Assert.Equal([bobNote.Id], (await bob.Client.GetAsync<TaskItemResponse>($"/api/tasks/{bobTask.Id}")).LinkedNotes.Select(summary => summary.Id));
        Assert.Equal([new TagResponse("private", 1)], await alice.Client.GetAsync<IReadOnlyList<TagResponse>>("/api/tags"));
    }
}
