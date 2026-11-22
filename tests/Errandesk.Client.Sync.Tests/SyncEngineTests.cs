using System.Text.Json;
using Errandesk.Client.Sync.Tests.Infrastructure;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private static readonly Guid s_defaultListId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    private readonly TemporaryDataService _local = new();
    private readonly ScriptedSyncTransport _transport = new();
    private readonly SyncEngine _engine;

    public SyncEngineTests() => _engine = new SyncEngine(_local.Data, _transport);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _local.Dispose();

    [Fact]
    public async Task FirstSync_StoresTheFullSnapshotAndReportsItAsAReset()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_defaultListId, "Buy milk", 3);
        var note = TestData.Note(Guid.CreateVersion7(), "Shopping", 4, [task.Id]);
        _transport.RespondToChanges = _ => TestData.Changes(5, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)], [task], [note]);
        ChangesResponse? raisedChanges = null;
        _local.Data.Changed += (_, changes) => raisedChanges = changes;

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Equal([0L], _transport.ChangeRequests);
        Assert.Equal(5, _local.Store.Version);
        Assert.True(_local.Data.HasSynchronized);
        Assert.Equal(TemporaryDataService.DefaultStartTime, _local.Data.LastSyncedAt);
        Assert.Equal(s_defaultListId, _local.Data.GetDefaultList()?.Id);
        var storedTask = Assert.Single(_local.Data.GetTasks(s_defaultListId.ToString(), includeCompleted: true));
        Assert.Equal("Shopping", Assert.Single(storedTask.LinkedNotes).Title);
        Assert.NotNull(raisedChanges);
        Assert.True(raisedChanges.ResetRequired);
        Assert.Equal("Shopping", Assert.Single(Assert.Single(raisedChanges.Tasks).LinkedNotes).Title);
    }

    [Fact]
    public async Task OfflineChanges_StayLocalUntilTheServerCanBeReached_ThenAreSentInOrderWithTheirOperationIds()
    {
        await SeedAsync();
        _transport.IsOffline = true;
        var list = _local.Data.CreateList("Groceries");
        var task = _local.Data.CreateTask(new CreateTaskRequest("Buy milk", ListId: list.Id, IsInMyDay: true));
        _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { IsImportant = true });
        _local.Data.AddStep(task.Id, "Check the fridge");

        var offlineOutcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Offline, offlineOutcome);
        Assert.Equal(4, _local.Data.PendingOperationCount);
        var firstOperation = _local.Store.GetOperations()[0];
        Assert.Equal(1, firstOperation.Attempts);
        Assert.Contains("No connection", firstOperation.LastError);
        var myDayTask = Assert.Single(_local.Data.GetTasks(TaskViews.MyDay, includeCompleted: true));
        Assert.True(myDayTask.IsImportant);
        Assert.True(myDayTask.IsInMyDay);
        Assert.Equal("Check the fridge", Assert.Single(myDayTask.Steps).Title);

        _transport.IsOffline = false;
        var onlineOutcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, onlineOutcome);
        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.Equal(["POST api/lists", "POST api/tasks", $"PATCH api/tasks/{task.Id}", $"POST api/tasks/{task.Id}/steps"], _transport.SentOperations.Select(operation => $"{operation.Method} {operation.Path}"));
        Assert.Equal(firstOperation.OperationId, _transport.SentOperations[0].OperationId);
        Assert.Equal(4, _transport.SentOperations.Select(operation => operation.OperationId).Distinct().Count());
        var createRequest = JsonSerializer.Deserialize(_transport.SentOperations[1].Body!, ErrandeskJsonSerializerContext.Default.CreateTaskRequest)!;
        Assert.Equal(task.Id, createRequest.Id);
        Assert.Equal(list.Id, createRequest.ListId);
        Assert.Equal(task.SortOrder, createRequest.SortOrder);
        Assert.True(createRequest.IsInMyDay);
    }

    [Fact]
    public async Task AcknowledgedOperation_StoresTheServerVersionOnlyWhenNoLaterLocalChangeWaits()
    {
        await SeedAsync();
        var task = _local.Data.CreateTask(new CreateTaskRequest("Buy milk"));
        _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { Title = "Buy oat milk" });
        var serverVersion = 10L;
        _transport.RespondToOperation = operation => ScriptedSyncTransport.Json(operation.Method == "POST" ? 201 : 200, TestData.Task(task.Id, s_defaultListId, operation.Method == "POST" ? "Buy milk" : "Buy oat milk", ++serverVersion) with { Body = "From the server" });
        var titlesAfterAcknowledgements = new List<string>();
        _transport.BeforeResponse = () => titlesAfterAcknowledgements.Add(_local.Data.GetTask(task.Id)!.Title);

        await _engine.SyncAsync(CancellationToken);

        Assert.Equal(["Buy oat milk", "Buy oat milk"], titlesAfterAcknowledgements);
        var storedTask = _local.Data.GetTask(task.Id)!;
        Assert.Equal("Buy oat milk", storedTask.Title);
        Assert.Equal("From the server", storedTask.Body);
        Assert.Equal(12, storedTask.ChangeVersion);
    }

    [Fact]
    public async Task ChangesFromTheServer_ForItemsWithPendingOperations_AreHeldBackAndTheItemIsReadAgainAfterSending()
    {
        await SeedAsync();
        var task = _local.Data.CreateTask(new CreateTaskRequest("Buy milk"));
        _transport.IsOffline = true;
        await _engine.SyncAsync(CancellationToken);
        _transport.IsOffline = false;

        _transport.RespondToOperation = _ => new SyncTransportResponse(503, null);
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, tasks: [TestData.Task(task.Id, s_defaultListId, "Changed elsewhere", 7)]), isFullSnapshot: false);

        Assert.Equal("Buy milk", _local.Data.GetTask(task.Id)!.Title);
        Assert.Equal([new RefreshItem(EntityTypes.Task, task.Id)], _local.Store.GetRefreshQueue());

        _transport.RespondToOperation = _ => ScriptedSyncTransport.Json(201, TestData.Task(task.Id, s_defaultListId, "Buy milk", 9));
        _transport.RespondToRead = _ => ScriptedSyncTransport.Json(200, TestData.Task(task.Id, s_defaultListId, "Changed elsewhere", 10));
        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Equal([$"api/tasks/{task.Id}"], _transport.ReadPaths);
        Assert.Empty(_local.Store.GetRefreshQueue());
        Assert.Equal("Changed elsewhere", _local.Data.GetTask(task.Id)!.Title);
    }

    [Fact]
    public async Task DeletionFromTheServer_OfAnItemWithPendingEdits_WinsAfterTheEditsAreRefused()
    {
        await SeedAsync([TestData.Task(Guid.Parse("018f0000-0000-7000-8000-0000000000a1"), s_defaultListId, "Old task", 2)]);
        var taskId = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");
        _local.Data.UpdateTask(taskId, new UpdateTaskRequest { Title = "Edited offline" });
        _local.Data.ApplyRemoteChanges(TestData.Changes(9, deletedTasks: [TestData.Tombstone(taskId, 9)]), isFullSnapshot: false);
        Assert.Equal("Edited offline", _local.Data.GetTask(taskId)!.Title);

        _transport.RespondToOperation = _ => new SyncTransportResponse(404, """{"status":404}""");
        _transport.RespondToRead = _ => new SyncTransportResponse(404, null);
        var deletedTaskIds = new List<Guid>();
        _local.Data.Changed += (_, changes) => deletedTaskIds.AddRange(changes.DeletedTasks.Select(tombstone => tombstone.Id));

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Null(_local.Data.GetTask(taskId));
        Assert.Equal([taskId], deletedTaskIds);
        Assert.Equal(0, _local.Data.PendingOperationCount);
    }

    [Fact]
    public async Task RefusedCreate_IsDroppedAndTheLocalItemRemovedWhenTheServerDoesNotHaveIt()
    {
        await SeedAsync();
        var task = _local.Data.CreateTask(new CreateTaskRequest("Invalid"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(400, """{"detail":"The title is too long."}""");

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.Null(_local.Data.GetTask(task.Id));
    }

    [Fact]
    public async Task DeleteAnsweredWith404_CountsAsDone()
    {
        await SeedAsync([TestData.Task(Guid.Parse("018f0000-0000-7000-8000-0000000000b1"), s_defaultListId, "Gone", 2)]);
        _local.Data.DeleteTask(Guid.Parse("018f0000-0000-7000-8000-0000000000b1"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(404, null);

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.Empty(_transport.ReadPaths);
    }

    [Theory]
    [InlineData(503, SyncOutcome.Offline)]
    [InlineData(429, SyncOutcome.RetryLater)]
    [InlineData(500, SyncOutcome.RetryLater)]
    public async Task TemporaryServerFailures_KeepTheOperationFirstAndStopSending(int statusCode, SyncOutcome expectedOutcome)
    {
        await SeedAsync();
        _local.Data.CreateTask(new CreateTaskRequest("First"));
        _local.Data.CreateTask(new CreateTaskRequest("Second"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(statusCode, null);

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(expectedOutcome, outcome);
        Assert.Single(_transport.SentOperations);
        Assert.Equal(2, _local.Data.PendingOperationCount);
        Assert.Equal(1, _local.Store.PeekOperation()!.Attempts);
        Assert.Empty(_transport.ChangeRequests);
    }

    [Fact]
    public async Task ConflictWithRetryAfter_IsRetriedWithoutLimit()
    {
        await SeedAsync();
        _local.Data.CreateTask(new CreateTaskRequest("Buy milk"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(409, null, HasRetryAfter: true);

        for (var attempt = 0; attempt < 5; attempt++) Assert.Equal(SyncOutcome.RetryLater, await _engine.SyncAsync(CancellationToken));

        Assert.Equal(1, _local.Data.PendingOperationCount);
        Assert.Equal(5, _local.Store.PeekOperation()!.Attempts);
    }

    [Fact]
    public async Task ConflictWithoutRetryAfter_IsDroppedAfterThreeAttempts()
    {
        await SeedAsync();
        var task = _local.Data.CreateTask(new CreateTaskRequest("Buy milk"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(409, """{"detail":"The ID belongs to a deleted task."}""");

        Assert.Equal(SyncOutcome.RetryLater, await _engine.SyncAsync(CancellationToken));
        Assert.Equal(SyncOutcome.RetryLater, await _engine.SyncAsync(CancellationToken));
        Assert.Equal(1, _local.Data.PendingOperationCount);
        Assert.Equal(SyncOutcome.Completed, await _engine.SyncAsync(CancellationToken));

        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.Null(_local.Data.GetTask(task.Id));
    }

    [Fact]
    public async Task ResetRequired_ReplacesSynchronizedItemsButKeepsItemsWithPendingOperations()
    {
        var keptTaskId = Guid.CreateVersion7();
        var droppedTaskId = Guid.CreateVersion7();
        await SeedAsync([TestData.Task(keptTaskId, s_defaultListId, "Kept", 2), TestData.Task(droppedTaskId, s_defaultListId, "Dropped", 3)]);
        var pendingTask = _local.Data.CreateTask(new CreateTaskRequest("Created offline"));
        _transport.RespondToOperation = _ => new SyncTransportResponse(503, null);
        await _engine.SyncAsync(CancellationToken);

        _local.Data.ApplyRemoteChanges(TestData.Changes(40, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)], [TestData.Task(keptTaskId, s_defaultListId, "Kept", 2)], resetRequired: true), isFullSnapshot: true);

        Assert.Equal(40, _local.Store.Version);
        Assert.NotNull(_local.Data.GetTask(keptTaskId));
        Assert.Null(_local.Data.GetTask(droppedTaskId));
        Assert.NotNull(_local.Data.GetTask(pendingTask.Id));
    }

    [Fact]
    public async Task ResetRequiredFromTheFeed_IsAppliedAsAFullSnapshot()
    {
        var droppedTaskId = Guid.CreateVersion7();
        await SeedAsync([TestData.Task(droppedTaskId, s_defaultListId, "Dropped", 3)]);
        _transport.RespondToChanges = _ => TestData.Changes(50, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)], resetRequired: true);
        var isReset = false;
        _local.Data.Changed += (_, changes) => isReset = changes.ResetRequired;

        await _engine.SyncAsync(CancellationToken);

        Assert.Null(_local.Data.GetTask(droppedTaskId));
        Assert.Equal(50, _local.Store.Version);
        Assert.True(isReset);
    }

    [Fact]
    public async Task OlderVersionsFromTheFeed_DoNotReplaceNewerStoredItems()
    {
        var taskId = Guid.CreateVersion7();
        await SeedAsync([TestData.Task(taskId, s_defaultListId, "Newest", 9)]);

        _local.Data.ApplyRemoteChanges(TestData.Changes(12, tasks: [TestData.Task(taskId, s_defaultListId, "Older", 8)]), isFullSnapshot: false);

        Assert.Equal("Newest", _local.Data.GetTask(taskId)!.Title);
        Assert.Equal(12, _local.Store.Version);
    }

    [Fact]
    public async Task SyncRequestedDuringARun_RunsAgainAfterIt()
    {
        await SeedAsync();
        var firstTask = _local.Data.CreateTask(new CreateTaskRequest("First"));
        Task<SyncOutcome>? secondRun = null;
        _transport.BeforeResponse = () =>
        {
            if (secondRun is not null) return;
            _local.Data.CreateTask(new CreateTaskRequest("Second"));
            secondRun = _engine.SyncAsync(CancellationToken);
        };

        var firstRun = _engine.SyncAsync(CancellationToken);
        await firstRun;

        Assert.Same(firstRun, secondRun);
        Assert.Equal(2, _transport.SentOperations.Count);
        Assert.Equal(firstTask.Id, _transport.SentOperations[0].EntityId);
        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.False(_engine.IsSyncing);
    }

    [Fact]
    public async Task ChangeFeedUnavailable_ReportsOffline()
    {
        await SeedAsync();
        _transport.RespondToChanges = _ => throw new SyncServerException(503);

        Assert.Equal(SyncOutcome.Offline, await _engine.SyncAsync(CancellationToken));
    }

    private async Task SeedAsync(IReadOnlyList<TaskItemResponse>? tasks = null)
    {
        var respondToChanges = _transport.RespondToChanges;
        _transport.RespondToChanges = _ => TestData.Changes(5, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)], tasks);
        Assert.Equal(SyncOutcome.Completed, await _engine.SyncAsync(CancellationToken));
        _transport.RespondToChanges = respondToChanges;
        _transport.ChangeRequests.Clear();
    }
}
