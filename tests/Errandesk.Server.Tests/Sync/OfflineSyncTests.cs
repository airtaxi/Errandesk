using System.Net;
using Errandesk.Client.Sync;
using Errandesk.Contracts;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Errandesk.Server.Tests.Sync;

public sealed class OfflineSyncTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task ChangesMadeOffline_AreReplayedOnTheServerAfterReconnecting()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        using var device = new SyncTestClient(server, alice);
        Assert.Equal(SyncOutcome.Completed, await device.SyncAsync());
        var defaultList = device.Data.GetDefaultList();
        Assert.NotNull(defaultList);

        device.Transport.IsOffline = true;
        var list = device.Data.CreateList("Groceries");
        var task = device.Data.CreateTask(new CreateTaskRequest("Buy milk", ListId: list.Id, IsInMyDay: true));
        device.Data.UpdateTask(task.Id, new UpdateTaskRequest { IsImportant = true, DueDate = new DateOnly(2031, 3, 14), Body = "Oat milk" });
        var withStep = device.Data.AddStep(task.Id, "Check the fridge");
        device.Data.UpdateStep(task.Id, withStep.Steps[0].Id, new UpdateTaskStepRequest { IsCompleted = true });
        device.Data.AddStep(task.Id, "Bring a bag");
        device.Data.UpdateList(list.Id, new UpdateTaskListRequest { Name = "Shopping" });
        var note = device.Data.CreateNote(new CreateNoteRequest("Recipe", "# Pancakes", Tags: ["#Food"], LinkedTaskIds: [task.Id]));
        device.Data.UpdateNote(note.Id, new UpdateNoteRequest { IsPinned = true });
        var discarded = device.Data.CreateTask(new CreateTaskRequest("Discarded"));
        device.Data.DeleteTask(discarded.Id);
        device.Data.UpdateTask(task.Id, new UpdateTaskRequest { Recurrence = new TaskRecurrence(RecurrenceFrequency.Weekly, 1, [DayOfWeek.Friday]) });

        Assert.Equal(SyncOutcome.Offline, await device.SyncAsync());
        Assert.Equal(12, device.Data.PendingOperationCount);

        device.Transport.IsOffline = false;
        Assert.Equal(SyncOutcome.Completed, await device.SyncAsync());

        Assert.Equal(0, device.Data.PendingOperationCount);
        Assert.Empty(device.Store.GetRefreshQueue());
        var lists = await alice.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        Assert.Equal(["Tasks", "Shopping"], lists.Select(serverList => serverList.Name));
        var serverTask = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.Equal(list.Id, serverTask.ListId);
        Assert.True(serverTask.IsImportant);
        Assert.True(serverTask.IsInMyDay);
        Assert.Equal("Oat milk", serverTask.Body);
        Assert.Equal(new DateOnly(2031, 3, 14), serverTask.DueDate);
        Assert.Equal(RecurrenceFrequency.Weekly, serverTask.Recurrence?.Frequency);
        Assert.Equal([("Check the fridge", true), ("Bring a bag", false)], serverTask.Steps.Select(step => (step.Title, step.IsCompleted)));
        Assert.Equal(withStep.Steps[0].Id, serverTask.Steps[0].Id);
        var serverNote = Assert.Single(serverTask.LinkedNotes);
        Assert.Equal(note.Id, serverNote.Id);
        Assert.True(serverNote.IsPinned);
        Assert.Equal(["food"], serverNote.Tags);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{discarded.Id}", null, HttpStatusCode.NotFound);
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));

        var localTask = device.Data.GetTask(task.Id)!;
        Assert.Equal(serverTask.ChangeVersion, localTask.ChangeVersion);
        Assert.Equal(serverTask.Steps, localTask.Steps);
        Assert.Equal(serverTask.SortOrder, localTask.SortOrder);
        Assert.Equal(serverTask.LinkedNotes.Select(linkedNote => linkedNote.Id), localTask.LinkedNotes.Select(linkedNote => linkedNote.Id));
        Assert.Equal(["Tasks", "Shopping"], device.Data.GetLists().Select(localList => localList.Name));
    }

    [Fact]
    public async Task OperationsWhoseResponsesWereLost_AreAppliedOnceWhenSentAgain()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        using var device = new SyncTestClient(server, alice);
        await device.SyncAsync();
        var task = device.Data.CreateTask(new CreateTaskRequest("Pay rent"));
        var withStep = device.Data.AddStep(task.Id, "Find the invoice");
        device.Data.UpdateTask(task.Id, new UpdateTaskRequest { IsCompleted = true });

        device.Transport.LosesResponses = true;
        Assert.Equal(SyncOutcome.Offline, await device.SyncAsync());
        Assert.Equal(SyncOutcome.Offline, await device.SyncAsync());
        device.Transport.LosesResponses = false;
        Assert.Equal(SyncOutcome.Completed, await device.SyncAsync());

        Assert.Equal(5, device.Transport.SentOperationCount);
        var serverTasks = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?includeCompleted=true");
        var serverTask = Assert.Single(serverTasks);
        Assert.True(serverTask.IsCompleted);
        Assert.Equal([withStep.Steps[0].Id], serverTask.Steps.Select(step => step.Id));
        Assert.True(device.Data.GetTask(task.Id)!.IsCompleted);
    }

    [Fact]
    public async Task PendingEdits_MergeWithEditsFromOtherDevicesPerField_AndDeletionElsewhereWins()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var laptop = await server.LoginAsync("alice");
        var merged = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Book flights"), HttpStatusCode.Created);
        var deleted = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Renew passport"), HttpStatusCode.Created);
        using var device = new SyncTestClient(server, alice);
        await device.SyncAsync();

        device.Transport.IsOffline = true;
        device.Data.UpdateTask(merged.Id, new UpdateTaskRequest { Title = "Book flights to Busan" });
        device.Data.UpdateTask(deleted.Id, new UpdateTaskRequest { IsImportant = true });
        await laptop.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{merged.Id}", new UpdateTaskRequest { IsImportant = true, Body = "Window seat" });
        await laptop.Client.DeleteAsync($"{TasksPath}/{deleted.Id}");
        device.Transport.IsOffline = false;

        Assert.Equal(SyncOutcome.Completed, await device.SyncAsync());

        var serverTask = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{merged.Id}");
        Assert.Equal("Book flights to Busan", serverTask.Title);
        Assert.True(serverTask.IsImportant);
        Assert.Equal("Window seat", serverTask.Body);
        var localTask = device.Data.GetTask(merged.Id)!;
        Assert.Equal((serverTask.Title, serverTask.IsImportant, serverTask.Body, serverTask.ChangeVersion), (localTask.Title, localTask.IsImportant, localTask.Body, localTask.ChangeVersion));
        Assert.Null(device.Data.GetTask(deleted.Id));
        Assert.Equal(0, device.Data.PendingOperationCount);
        Assert.Empty(device.Store.GetRefreshQueue());
    }

    [Fact]
    public async Task ChangesFromOtherDevices_ArePulledAfterTheOutbox()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        using var phone = new SyncTestClient(server, alice);
        using var desktop = new SyncTestClient(server, alice);
        await phone.SyncAsync();
        await desktop.SyncAsync();

        var task = phone.Data.CreateTask(new CreateTaskRequest("Water the plants"));
        var note = phone.Data.CreateNote(new CreateNoteRequest("Plants", LinkedTaskIds: [task.Id]));
        await phone.SyncAsync();
        var changes = new List<ChangesResponse>();
        desktop.Data.Changed += (_, raisedChanges) => changes.Add(raisedChanges);
        await desktop.SyncAsync();

        Assert.Equal("Water the plants", desktop.Data.GetTask(task.Id)?.Title);
        Assert.Equal(note.Id, Assert.Single(desktop.Data.GetTask(task.Id)!.LinkedNotes).Id);
        Assert.Contains(changes, raisedChanges => raisedChanges.Tasks.Any(changedTask => changedTask.Id == task.Id && changedTask.LinkedNotes.Count == 1));

        phone.Data.DeleteTask(task.Id);
        await phone.SyncAsync();
        await desktop.SyncAsync();

        Assert.Null(desktop.Data.GetTask(task.Id));
        Assert.Equal(server.TimeProvider.GetUtcNow(), desktop.Data.LastSyncedAt);
    }

    [Fact]
    public async Task ClientsThatMissedPurgedDeletions_ReplaceTheirDataWithTheFullSnapshot()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var kept = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Kept"), HttpStatusCode.Created);
        var removed = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Removed elsewhere"), HttpStatusCode.Created);
        using var device = new SyncTestClient(server, alice);
        await device.SyncAsync();
        var createdOffline = device.Data.CreateTask(new CreateTaskRequest("Created while away"));
        device.Transport.IsOffline = true;
        await device.SyncAsync();

        await alice.Client.DeleteAsync($"{TasksPath}/{removed.Id}");
        await server.AdvanceTimeAsync(TimeSpan.FromDays(20), alice);
        await server.AdvanceTimeAsync(TimeSpan.FromDays(11), alice);
        Assert.Equal(1L, await server.Services.GetRequiredService<TombstonePurger>().PurgeAsync(TestContext.Current.CancellationToken));
        var isReset = false;
        device.Data.Changed += (_, changes) => isReset |= changes.ResetRequired;
        device.Transport.IsOffline = false;

        Assert.Equal(SyncOutcome.Completed, await device.SyncAsync());

        Assert.True(isReset);
        Assert.NotNull(device.Data.GetTask(kept.Id));
        Assert.Null(device.Data.GetTask(removed.Id));
        Assert.NotNull(device.Data.GetTask(createdOffline.Id));
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{createdOffline.Id}");
    }
}
