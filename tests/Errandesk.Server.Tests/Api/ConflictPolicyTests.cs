using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class ConflictPolicyTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";
    private const string NotesPath = "/api/notes";

    [Fact]
    public async Task TaskPatches_WithoutExpectedVersion_ApplyOnlyTheSentFieldsAndTheLastWriterWinsPerField()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var phone = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var task = await phone.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk", Body: "From the corner shop"), HttpStatusCode.Created);

        await phone.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Buy oat milk" });
        await laptop.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsImportant = true, DueDate = new DateOnly(2031, 3, 12) });
        await phone.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Body = "From the market" });
        var final = await laptop.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Buy soy milk" });

        Assert.Equal("Buy soy milk", final.Title);
        Assert.Equal("From the market", final.Body);
        Assert.True(final.IsImportant);
        Assert.Equal(new DateOnly(2031, 3, 12), final.DueDate);
        JsonAssert.Equal(final, await phone.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}"));
    }

    [Fact]
    public async Task NotePatches_WithoutExpectedVersion_ApplyOnlyTheSentFields()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var phone = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var note = await phone.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Trip", "# Busan"), HttpStatusCode.Created);

        await phone.Client.PatchAsync<NoteResponse>($"{NotesPath}/{note.Id}", new UpdateNoteRequest { Content = "# Busan\n- Beach" });
        await laptop.Client.PatchAsync<NoteResponse>($"{NotesPath}/{note.Id}", new UpdateNoteRequest { Title = "Busan trip", IsPinned = true });
        var final = await phone.Client.GetAsync<NoteResponse>($"{NotesPath}/{note.Id}");

        Assert.Equal("Busan trip", final.Title);
        Assert.Equal("# Busan\n- Beach", final.Content);
        Assert.True(final.IsPinned);
    }

    [Fact]
    public async Task Deletion_WinsOverLaterTaskEdits()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var phone = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var task = await phone.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk", Steps: [new CreateTaskStepRequest("Check the fridge")]), HttpStatusCode.Created);
        var since = (await phone.Client.GetAsync<ChangesResponse>("/api/changes?since=0")).CurrentVersion;

        await phone.Client.DeleteAsync($"{TasksPath}/{task.Id}");

        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Buy oat milk" }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true, ExpectedChangeVersion = task.ChangeVersion }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Go shopping"), HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}/steps/{task.Steps[0].Id}", new UpdateTaskStepRequest { IsCompleted = true }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Buy milk", Id: task.Id), HttpStatusCode.Conflict);

        var stored = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal("Buy milk", stored.Title);
        Assert.False(stored.IsCompleted);
        var changes = await laptop.Client.GetAsync<ChangesResponse>($"/api/changes?since={since}");
        Assert.Empty(changes.Tasks);
        Assert.Equal(task.Id, Assert.Single(changes.DeletedTasks).Id);
    }

    [Fact]
    public async Task Deletion_WinsOverLaterNoteAndListEdits()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var phone = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var note = await phone.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Trip"), HttpStatusCode.Created);
        var list = await phone.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var taskInList = await phone.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: list.Id), HttpStatusCode.Created);
        var otherTask = await phone.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants"), HttpStatusCode.Created);

        await phone.Client.DeleteAsync($"{NotesPath}/{note.Id}");
        await phone.Client.DeleteAsync($"/api/lists/{list.Id}");

        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{NotesPath}/{note.Id}", new UpdateNoteRequest { Title = "Busan trip" }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Put, $"{NotesPath}/{note.Id}/linked-tasks/{otherTask.Id}", null, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"/api/lists/{list.Id}", new UpdateTaskListRequest { Name = "Moved" }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{taskInList.Id}", new UpdateTaskRequest { IsImportant = true }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{otherTask.Id}", new UpdateTaskRequest { ListId = list.Id }, HttpStatusCode.NotFound);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Label boxes", ListId: list.Id), HttpStatusCode.NotFound);

        Assert.True((await server.FindRawAsync(server.Database.Notes, note.Id)).IsDeleted);
        Assert.Equal("Moving", (await server.FindRawAsync(server.Database.TaskLists, list.Id)).Name);
        Assert.False((await server.FindRawAsync(server.Database.Tasks, taskInList.Id)).IsImportant);
        Assert.NotEqual(list.Id, (await laptop.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{otherTask.Id}")).ListId);
    }

    [Fact]
    public async Task Deletion_WinsOverConcurrentEdits()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var phone = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var task = await phone.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);

        var edits = Enumerable.Range(0, 8).Select(index => laptop.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = $"Edit {index}" })).ToList();
        using var deletion = await phone.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}");
        foreach (var edit in edits) (await edit).Dispose();

        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        Assert.True((await server.FindRawAsync(server.Database.Tasks, task.Id)).IsDeleted);
        await laptop.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
    }
}
