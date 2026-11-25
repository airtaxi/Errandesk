using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Data;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Agent;

public sealed class AgentToolsetTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";
    private const string NotesPath = "/api/notes";

    [Fact]
    public async Task CreateTools_ExposesEveryToolAndRequiresApprovalForDeletes()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var tools = CreateToolset(server, alice.Id).CreateTools();

        string[] expectedNames = [AgentToolNames.ListTaskLists, AgentToolNames.CreateTaskList, AgentToolNames.SearchTasks, AgentToolNames.GetMyDay, AgentToolNames.GetOverdueTasks, AgentToolNames.CreateTask, AgentToolNames.UpdateTask, AgentToolNames.CompleteTask, AgentToolNames.AddStep, AgentToolNames.CompleteStep, AgentToolNames.AddToMyDay, AgentToolNames.SearchNotes, AgentToolNames.ReadNote, AgentToolNames.CreateNote, AgentToolNames.UpdateNote, AgentToolNames.EditNoteContent, AgentToolNames.LinkNoteToTask, AgentToolNames.GetCurrentTime, AgentToolNames.SetReminder, AgentToolNames.SetRecurrence, AgentToolNames.DeleteTask, AgentToolNames.DeleteTaskList, AgentToolNames.DeleteNote];
        Assert.Equal(expectedNames.Order(), tools.Select(tool => tool.Name).Order());
        Assert.Equal([AgentToolNames.DeleteNote, AgentToolNames.DeleteTask, AgentToolNames.DeleteTaskList], tools.OfType<ApprovalRequiredAIFunction>().Select(tool => tool.Name).Order());
        var createTask = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools, tool => tool.Name == AgentToolNames.CreateTask));
        Assert.Contains("\"title\"", createTask.JsonSchema.GetRawText());
        Assert.Contains("\"dueDate\"", createTask.JsonSchema.GetRawText());
    }

    [Fact]
    public async Task CreateTask_CreatesThroughTheServiceAndRecordsAnUndoableAction()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        List<string> events = [];
        var context = new AgentRunContext(alice.Id, null)
        {
            ToolStarted = data => Record(events, $"started:{data.ToolName}"),
            ToolCompleted = data => Record(events, $"completed:{data.ToolName}:{data.Succeeded}"),
        };
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var dueDate = new DateOnly(2031, 3, 12);

        var task = Assert.IsType<TaskItemResponse>(await toolset.CreateTaskAsync("  Buy milk ", dueDate: dueDate, isImportant: true, addToMyDay: true, note: "Low fat", steps: ["Check fridge"], cancellationToken: TestContext.Current.CancellationToken));

        var defaultList = await server.GetDefaultListAsync(alice);
        JsonAssert.Equal(task, await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}"));
        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(defaultList.Id, task.ListId);
        Assert.Equal(dueDate, task.DueDate);
        Assert.True(task.IsImportant);
        Assert.True(task.IsInMyDay);
        Assert.Equal("Low fat", task.Body);
        Assert.Equal("Check fridge", Assert.Single(task.Steps).Title);
        Assert.Equal(["started:create_task", "completed:create_task:True"], events);

        var toolCall = Assert.Single(context.ToolCalls);
        Assert.True(toolCall.Succeeded);
        var action = await server.FindRawAsync(server.Database.AgentActions, Assert.NotNull(toolCall.AgentActionId));
        Assert.Equal(alice.Id, action.OwnerUserId);
        Assert.Equal(AgentToolNames.CreateTask, action.ToolName);
        Assert.Equal(EntityTypes.Task, action.EntityType);
        Assert.Equal(task.Id, action.EntityId);
        var entity = Assert.Single(action.Entities);
        Assert.Null(entity.Before);
        Assert.Equal(task.ChangeVersion, BsonSerializer.Deserialize<TaskDocument>(entity.After).ChangeVersion);
        Assert.Null(action.UndoneAt);
    }

    [Fact]
    public async Task TaskChangeTools_ChangeOnlyGivenValuesAndRecordBeforeAndAfter()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var errands = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Errands"), HttpStatusCode.Created);
        var created = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call bank", Body: "About the card", DueDate: new DateOnly(2031, 3, 15)), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, Guid.CreateVersion7());
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var cancellationToken = TestContext.Current.CancellationToken;

        var updated = Assert.IsType<TaskItemResponse>(await toolset.UpdateTaskAsync(created.Id, title: "Call the bank", removeDueDate: true, listId: errands.Id, cancellationToken: cancellationToken));
        Assert.Equal("Call the bank", updated.Title);
        Assert.Equal("About the card", updated.Body);
        Assert.Null(updated.DueDate);
        Assert.Equal(errands.Id, updated.ListId);

        var withStep = Assert.IsType<TaskItemResponse>(await toolset.AddStepAsync(created.Id, "Find card number", cancellationToken));
        var step = Assert.Single(withStep.Steps);
        Assert.Equal("Find card number", step.Title);
        var withCompletedStep = Assert.IsType<TaskItemResponse>(await toolset.CompleteStepAsync(created.Id, step.Id, cancellationToken: cancellationToken));
        Assert.True(Assert.Single(withCompletedStep.Steps).IsCompleted);
        Assert.False(withCompletedStep.IsCompleted);
        Assert.True(Assert.IsType<TaskItemResponse>(await toolset.AddToMyDayAsync(created.Id, cancellationToken: cancellationToken)).IsInMyDay);
        var completed = Assert.IsType<TaskItemResponse>(await toolset.CompleteTaskAsync(created.Id, cancellationToken: cancellationToken));
        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.CompletedAt);
        JsonAssert.Equal(completed, await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{created.Id}"));

        Assert.Equal(5, context.ToolCalls.Count);
        Assert.All(context.ToolCalls, toolCall => Assert.NotNull(toolCall.AgentActionId));
        var updateAction = await server.FindRawAsync(server.Database.AgentActions, context.ToolCalls[0].AgentActionId!.Value);
        Assert.Equal(context.ConversationId, updateAction.ConversationId);
        var entity = Assert.Single(updateAction.Entities);
        var before = BsonSerializer.Deserialize<TaskDocument>(entity.Before!);
        var after = BsonSerializer.Deserialize<TaskDocument>(entity.After);
        Assert.Equal("Call bank", before.Title);
        Assert.Equal(new DateOnly(2031, 3, 15), before.DueDate);
        Assert.Equal("Call the bank", after.Title);
        Assert.Null(after.DueDate);
        Assert.Equal(created.ChangeVersion, before.ChangeVersion);
        Assert.Equal(updated.ChangeVersion, after.ChangeVersion);
    }

    [Fact]
    public async Task ReadTools_SearchTasksAndReturnMyDayAndOverdueTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var today = new DateOnly(2031, 3, 10);
        var overdue = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay electricity bill", DueDate: today.AddDays(-2)), HttpStatusCode.Created);
        var dueToday = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay rent", DueDate: today, IsInMyDay: true), HttpStatusCode.Created);
        var withStep = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Groceries", Steps: [new CreateTaskStepRequest("Buy electricity adapter")]), HttpStatusCode.Created);
        var completed = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Electricity meter photo", IsCompleted: true), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var cancellationToken = TestContext.Current.CancellationToken;

        var found = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.SearchTasksAsync("ELECTRICITY", cancellationToken: cancellationToken));
        Assert.Equal([overdue.Id, withStep.Id], found.Select(task => task.Id).Order());
        var foundWithCompleted = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.SearchTasksAsync("electricity", includeCompleted: true, cancellationToken: cancellationToken));
        Assert.Contains(foundWithCompleted, task => task.Id == completed.Id);
        var everyWord = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.SearchTasksAsync("pay bill", cancellationToken: cancellationToken));
        Assert.Equal(overdue.Id, Assert.Single(everyWord).Id);

        var myDay = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.GetMyDayAsync(cancellationToken));
        Assert.Equal(dueToday.Id, Assert.Single(myDay).Id);
        var overdueTasks = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.GetOverdueTasksAsync(cancellationToken));
        Assert.Equal(overdue.Id, Assert.Single(overdueTasks).Id);

        var lists = Assert.IsAssignableFrom<IReadOnlyList<TaskListResponse>>(await toolset.ListTaskListsAsync(cancellationToken));
        Assert.True(Assert.Single(lists).IsDefault);
        var currentTime = Assert.IsType<AgentCurrentTime>(await toolset.GetCurrentTimeAsync(cancellationToken));
        Assert.Equal("2031-03-10T21:00", currentTime.LocalDateTime);
        Assert.Equal("Monday", currentTime.DayOfWeek);
        Assert.Equal("Asia/Seoul", currentTime.TimeZone);
        Assert.Equal("+09:00", currentTime.UtcOffset);

        Assert.Equal(7, context.ToolCalls.Count);
        Assert.All(context.ToolCalls, toolCall => Assert.Null(toolCall.AgentActionId));
        Assert.Equal(0, await server.Database.AgentActions.CountDocumentsAsync(FilterDefinition<AgentActionDocument>.Empty, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task NoteTools_CreateSearchReadUpdateAndLinkThroughTheService()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plan trip"), HttpStatusCode.Created);
        var otherTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Book hotel"), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var cancellationToken = TestContext.Current.CancellationToken;

        var note = Assert.IsType<NoteResponse>(await toolset.CreateNoteAsync("Busan trip", "# Day 1\n\nHaeundae beach", ["#Travel", "korea"], [task.Id], cancellationToken));
        Assert.Equal(["travel", "korea"], note.Tags);
        Assert.Equal([task.Id], note.LinkedTaskIds);
        JsonAssert.Equal(note, await alice.Client.GetAsync<NoteResponse>($"{NotesPath}/{note.Id}"));

        var summaries = Assert.IsAssignableFrom<IReadOnlyList<NoteSummaryResponse>>(await toolset.SearchNotesAsync("haeundae", cancellationToken: cancellationToken));
        Assert.Equal("# Day 1 Haeundae beach", Assert.Single(summaries).Excerpt);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NoteSummaryResponse>>(await toolset.SearchNotesAsync(tag: "travel", cancellationToken: cancellationToken)));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NoteSummaryResponse>>(await toolset.SearchNotesAsync(linkedTaskId: task.Id, cancellationToken: cancellationToken)));
        Assert.Equal(note.Content, Assert.IsType<NoteResponse>(await toolset.ReadNoteAsync(note.Id, cancellationToken)).Content);

        var updated = Assert.IsType<NoteResponse>(await toolset.UpdateNoteAsync(note.Id, content: "# Day 1\n\nGwangalli", isPinned: true, cancellationToken: cancellationToken));
        Assert.Equal("Busan trip", updated.Title);
        Assert.Equal("# Day 1\n\nGwangalli", updated.Content);
        Assert.Equal(["travel", "korea"], updated.Tags);
        Assert.True(updated.IsPinned);

        var edited = Assert.IsType<NoteResponse>(await toolset.EditNoteContentAsync(note.Id, "Gwangalli", "Gwangalli beach at night", cancellationToken: cancellationToken));
        Assert.Equal("# Day 1\n\nGwangalli beach at night", edited.Content);
        Assert.Equal("Busan trip", edited.Title);
        Assert.IsType<AgentToolError>(await toolset.EditNoteContentAsync(note.Id, "Haeundae", "Songjeong", cancellationToken: cancellationToken));
        Assert.IsType<AgentToolError>(await toolset.EditNoteContentAsync(note.Id, "a", "o", cancellationToken: cancellationToken));
        var editedEverywhere = Assert.IsType<NoteResponse>(await toolset.EditNoteContentAsync(note.Id, "Day", "Night", replaceAll: true, cancellationToken: cancellationToken));
        Assert.Equal("# Night 1\n\nGwangalli beach at night", editedEverywhere.Content);

        var linked = Assert.IsType<NoteResponse>(await toolset.LinkNoteToTaskAsync(note.Id, otherTask.Id, cancellationToken));
        Assert.Equal([task.Id, otherTask.Id], linked.LinkedTaskIds);
        await toolset.LinkNoteToTaskAsync(note.Id, otherTask.Id, cancellationToken);

        var changeCalls = context.ToolCalls.Where(toolCall => toolCall.ToolName is AgentToolNames.CreateNote or AgentToolNames.UpdateNote or AgentToolNames.LinkNoteToTask).ToList();
        Assert.Equal(4, changeCalls.Count);
        Assert.All(changeCalls.Take(3), toolCall => Assert.NotNull(toolCall.AgentActionId));
        Assert.Null(changeCalls[3].AgentActionId);
    }

    [Fact]
    public async Task Tools_ReturnDomainErrorsToTheModelInsteadOfThrowing()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob", (await alice.Client.PostAsync<InviteResponse>("/api/admin/invites", new CreateInviteRequest(), HttpStatusCode.Created)).Code);
        var bobsTask = await bob.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Bob's task"), HttpStatusCode.Created);
        var defaultList = await server.GetDefaultListAsync(alice);
        List<ToolCompletedEventData> completions = [];
        var context = new AgentRunContext(alice.Id, null) { ToolCompleted = data => Record(completions, data) };
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal("The task was not found.", Assert.IsType<AgentToolError>(await toolset.UpdateTaskAsync(Guid.CreateVersion7(), title: "Nothing", cancellationToken: cancellationToken)).Error);
        Assert.Equal("The task was not found.", Assert.IsType<AgentToolError>(await toolset.CompleteTaskAsync(bobsTask.Id, cancellationToken: cancellationToken)).Error);
        Assert.Equal("The task was not found.", Assert.IsType<AgentToolError>(await toolset.DeleteTaskAsync(bobsTask.Id, cancellationToken)).Error);
        Assert.Equal("The default list cannot be deleted.", Assert.IsType<AgentToolError>(await toolset.DeleteTaskListAsync(defaultList.Id, cancellationToken)).Error);
        Assert.IsType<AgentToolError>(await toolset.CreateTaskAsync("   ", cancellationToken: cancellationToken));
        Assert.IsType<AgentToolError>(await toolset.ReadNoteAsync(Guid.CreateVersion7(), cancellationToken));

        Assert.Equal(6, completions.Count);
        Assert.All(completions, completion => Assert.False(completion.Succeeded));
        Assert.All(completions, completion => Assert.Null(completion.AgentActionId));
        Assert.All(completions, completion => Assert.False(string.IsNullOrEmpty(completion.Error)));
        Assert.False((await bob.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{bobsTask.Id}")).IsCompleted);
        Assert.Equal(0, await server.Database.AgentActions.CountDocumentsAsync(FilterDefinition<AgentActionDocument>.Empty, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task DeleteTools_DeleteThroughTheServiceAndSnapshotEveryAffectedItem()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Moving"), HttpStatusCode.Created);
        var firstTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack books", ListId: list.Id), HttpStatusCode.Created);
        var secondTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack dishes", ListId: list.Id), HttpStatusCode.Created);
        var standaloneTask = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>(NotesPath, new CreateNoteRequest("Old note"), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(new AgentDeletionResult(EntityTypes.List, list.Id, "Moving"), await toolset.DeleteTaskListAsync(list.Id, cancellationToken));
        Assert.Equal(new AgentDeletionResult(EntityTypes.Task, standaloneTask.Id, "Water plants"), await toolset.DeleteTaskAsync(standaloneTask.Id, cancellationToken));
        Assert.Equal(new AgentDeletionResult(EntityTypes.Note, note.Id, "Old note"), await toolset.DeleteNoteAsync(note.Id, cancellationToken));

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/lists/{list.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{firstTask.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{standaloneTask.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{NotesPath}/{note.Id}", null, HttpStatusCode.NotFound);

        var listAction = await server.FindRawAsync(server.Database.AgentActions, context.ToolCalls[0].AgentActionId!.Value);
        Assert.Equal(EntityTypes.List, listAction.EntityType);
        Assert.Equal(list.Id, listAction.EntityId);
        Assert.Equal([EntityTypes.List, EntityTypes.Task, EntityTypes.Task], listAction.Entities.Select(entity => entity.EntityType));
        Assert.Equal([firstTask.Id, secondTask.Id], listAction.Entities.Skip(1).Select(entity => entity.EntityId).Order());
        Assert.All(listAction.Entities, entity => Assert.False(BsonSerializer.Deserialize<TaskListDocument>(entity.Before!).IsDeleted));
        Assert.All(listAction.Entities, entity => Assert.True(BsonSerializer.Deserialize<TaskListDocument>(entity.After).IsDeleted));
    }

    private static AgentToolset CreateToolset(ApiTestServer server, Guid userId) => server.Services.GetRequiredService<AgentToolsetFactory>().Create(new AgentRunContext(userId, null));

    private static Task Record<TItem>(List<TItem> items, TItem item)
    {
        items.Add(item);
        return Task.CompletedTask;
    }
}
