using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Errandesk.Server.Tests.Agent;

public sealed class AgentActionUndoTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Undo_OfCreate_DeletesTheCreatedTaskAndCanRunOnlyOnce()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var context = new AgentRunContext(alice.Id, null);
        var task = Assert.IsType<TaskItemResponse>(await CreateToolset(server, context).CreateTaskAsync("Buy milk", cancellationToken: TestContext.Current.CancellationToken));
        var actionId = context.ToolCalls[0].AgentActionId!.Value;

        server.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        var undone = await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, UndoPath(actionId));

        Assert.Equal(actionId, undone.Id);
        Assert.Equal(AgentToolNames.CreateTask, undone.ToolName);
        Assert.Equal(EntityTypes.Task, undone.EntityType);
        Assert.Equal(task.Id, undone.EntityId);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddSeconds(5), undone.UndoneAt);
        JsonAssert.Equal(undone, await alice.Client.GetAsync<AgentActionResponse>($"/api/agent-actions/{actionId}"));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
        var changes = await alice.Client.GetAsync<ChangesResponse>($"/api/changes?since={task.ChangeVersion}");
        Assert.Equal(task.Id, Assert.Single(changes.DeletedTasks).Id);

        var problem = await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, UndoPath(actionId), null, HttpStatusCode.Conflict);
        Assert.Equal("The action was already undone.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Undo_OfUpdate_RestoresThePreviousValuesWithANewChangeVersion()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var original = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call bank", Body: "About the card", DueDate: new DateOnly(2031, 3, 15), Steps: [new CreateTaskStepRequest("Find number")]), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = CreateToolset(server, context);
        var updated = Assert.IsType<TaskItemResponse>(await toolset.UpdateTaskAsync(original.Id, title: "Call the bank", isImportant: true, removeDueDate: true, cancellationToken: TestContext.Current.CancellationToken));

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, UndoPath(context.ToolCalls[0].AgentActionId!.Value));

        var restored = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{original.Id}");
        Assert.Equal("Call bank", restored.Title);
        Assert.False(restored.IsImportant);
        Assert.Equal(new DateOnly(2031, 3, 15), restored.DueDate);
        Assert.Equal(original.Steps, restored.Steps);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.True(restored.ChangeVersion > updated.ChangeVersion);
    }

    [Fact]
    public async Task Undo_OfDelete_RestoresTheTaskWithItsLinkedNotes()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plan trip"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Itinerary", LinkedTaskIds: [task.Id]), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        await CreateToolset(server, context).DeleteTaskAsync(task.Id, TestContext.Current.CancellationToken);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, UndoPath(context.ToolCalls[0].AgentActionId!.Value));

        var restored = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.Equal("Plan trip", restored.Title);
        Assert.Equal(note.Id, Assert.Single(restored.LinkedNotes).Id);
        Assert.Equal([task.Id], (await alice.Client.GetAsync<NoteResponse>($"/api/notes/{note.Id}")).LinkedTaskIds);
    }

    [Fact]
    public async Task Undo_OfListDelete_RestoresTheListAndTheTasksDeletedWithIt()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var openTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: list.Id), HttpStatusCode.Created);
        var completedTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Order boxes", ListId: list.Id, IsCompleted: true), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        await CreateToolset(server, context).DeleteTaskListAsync(list.Id, TestContext.Current.CancellationToken);

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, UndoPath(context.ToolCalls[0].AgentActionId!.Value));

        Assert.Equal("Moving", (await alice.Client.GetAsync<TaskListResponse>($"/api/lists/{list.Id}")).Name);
        var tasks = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={list.Id}&includeCompleted=true");
        Assert.Equal([openTask.Id, completedTask.Id], tasks.Select(task => task.Id));
        Assert.True(tasks[1].IsCompleted);
    }

    [Fact]
    public async Task Undo_WhenTheItemChangedSince_ReturnsConflictAndChangesNothing()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call bank"), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        await CreateToolset(server, context).UpdateTaskAsync(task.Id, title: "Call the bank", cancellationToken: TestContext.Current.CancellationToken);
        var actionId = context.ToolCalls[0].AgentActionId!.Value;
        var edited = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsImportant = true });

        var problem = await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, UndoPath(actionId), null, HttpStatusCode.Conflict);

        Assert.Equal("The task was changed after the assistant changed it.", problem.GetProperty("detail").GetString());
        JsonAssert.Equal(edited, await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}"));
        Assert.Null((await alice.Client.GetAsync<AgentActionResponse>($"/api/agent-actions/{actionId}")).UndoneAt);
    }

    [Fact]
    public async Task Undo_OfTaskDeleteWhoseListWasDeleted_ReturnsConflict()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: list.Id), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        await CreateToolset(server, context).DeleteTaskAsync(task.Id, TestContext.Current.CancellationToken);
        await alice.Client.DeleteAsync($"/api/lists/{list.Id}");

        var problem = await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, UndoPath(context.ToolCalls[0].AgentActionId!.Value), null, HttpStatusCode.Conflict);

        Assert.Equal("The list of the task was deleted.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Undo_OfAnotherUsersActionOrAnUnknownAction_ReturnsNotFound()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var context = new AgentRunContext(alice.Id, null);
        var task = Assert.IsType<TaskItemResponse>(await CreateToolset(server, context).CreateTaskAsync("Buy milk", cancellationToken: TestContext.Current.CancellationToken));
        var actionId = context.ToolCalls[0].AgentActionId!.Value;

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, UndoPath(actionId), null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/agent-actions/{actionId}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, UndoPath(Guid.CreateVersion7()), null, HttpStatusCode.NotFound);
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
    }

    private static string UndoPath(Guid actionId) => $"/api/agent-actions/{actionId}/undo";

    private static AgentToolset CreateToolset(ApiTestServer server, AgentRunContext context) => server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
}
