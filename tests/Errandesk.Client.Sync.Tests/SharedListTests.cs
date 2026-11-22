using Errandesk.Client.Sync.Tests.Infrastructure;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests;

public sealed class SharedListTests : IDisposable
{
    private static readonly Guid s_defaultListId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid s_sharedListId = Guid.Parse("018f0000-0000-7000-8000-000000000002");
    private static readonly TaskListMemberResponse s_owner = new(Guid.Parse("018f0000-0000-7000-8000-0000000000f1"), "alice", "Alice", TestData.CreatedAt);
    private static readonly TaskListMemberResponse s_member = new(Guid.Parse("018f0000-0000-7000-8000-0000000000f2"), "bob", "Bob", TestData.CreatedAt);

    private readonly TemporaryDataService _local = new();
    private readonly ScriptedSyncTransport _transport = new();
    private readonly SyncEngine _engine;

    public SharedListTests()
    {
        _engine = new SyncEngine(_local.Data, _transport);
        _local.Data.ApplyRemoteChanges(TestData.Changes(5, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)]), isFullSnapshot: true);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _local.Dispose();

    [Fact]
    public async Task GainingAccess_StoresTheSharedListWithItsOwnerMembersAndTasks()
    {
        var firstTask = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 2);
        var secondTask = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy bread", 3);
        _transport.RespondToChanges = _ => TestData.Changes(10, [SharedList(9)], [firstTask, secondTask]);
        ChangesResponse? raisedChanges = null;
        _local.Data.Changed += (_, changes) => raisedChanges = changes;

        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        var storedList = _local.Data.GetList(s_sharedListId);
        Assert.NotNull(storedList);
        Assert.Equal(s_owner.UserId, storedList.Owner?.UserId);
        Assert.Equal([s_member.UserId], storedList.Members!.Select(member => member.UserId));
        Assert.Equal(2, _local.Data.GetTasks(s_sharedListId.ToString(), includeCompleted: true).Count);
        Assert.Equal([s_sharedListId], raisedChanges!.Lists.Select(list => list.Id));
        Assert.Equal(2, raisedChanges.Tasks.Count);
    }

    [Fact]
    public void LosingAccess_RemovesTheListAndEveryStoredTaskOfIt()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 7);
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, [SharedList(6)], [task]), isFullSnapshot: false);
        ChangesResponse? raisedChanges = null;
        _local.Data.Changed += (_, changes) => raisedChanges = changes;

        _local.Data.ApplyRemoteChanges(TestData.Changes(12, deletedLists: [TestData.Tombstone(s_sharedListId, 11)]), isFullSnapshot: false);

        Assert.Null(_local.Data.GetList(s_sharedListId));
        Assert.Null(_local.Data.GetTask(task.Id));
        Assert.Equal([s_sharedListId], raisedChanges!.DeletedLists.Select(tombstone => tombstone.Id));
        Assert.Equal([task.Id], raisedChanges.DeletedTasks.Select(tombstone => tombstone.Id));
    }

    [Fact]
    public void RegainingAccess_AfterRemoval_StoresTheTasksAgain()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 7);
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, [SharedList(6)], [task]), isFullSnapshot: false);
        _local.Data.ApplyRemoteChanges(TestData.Changes(12, deletedLists: [TestData.Tombstone(s_sharedListId, 11)], deletedTasks: [TestData.Tombstone(task.Id, 11)]), isFullSnapshot: false);

        _local.Data.ApplyRemoteChanges(TestData.Changes(15, [SharedList(14)], [task]), isFullSnapshot: false);

        Assert.NotNull(_local.Data.GetList(s_sharedListId));
        Assert.Equal("Buy milk", _local.Data.GetTask(task.Id)?.Title);
    }

    [Fact]
    public async Task LosingAccess_WithPendingEdits_DropsTheTaskAfterTheServerRefusesTheEdit()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 7);
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, [SharedList(6)], [task]), isFullSnapshot: false);
        _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { Title = "Buy oat milk" });
        _local.Data.ApplyRemoteChanges(TestData.Changes(12, deletedLists: [TestData.Tombstone(s_sharedListId, 11)], deletedTasks: [TestData.Tombstone(task.Id, 11)]), isFullSnapshot: false);
        Assert.Null(_local.Data.GetList(s_sharedListId));
        Assert.Equal("Buy oat milk", _local.Data.GetTask(task.Id)?.Title);

        _transport.RespondToOperation = _ => new SyncTransportResponse(404, """{"status":404}""");
        _transport.RespondToRead = _ => new SyncTransportResponse(404, null);
        var outcome = await _engine.SyncAsync(CancellationToken);

        Assert.Equal(SyncOutcome.Completed, outcome);
        Assert.Null(_local.Data.GetTask(task.Id));
        Assert.Equal(0, _local.Data.PendingOperationCount);
        Assert.Empty(_local.Store.GetRefreshQueue());
    }

    [Fact]
    public void ServerList_IsStoredWhenNewerAndLeftAloneWhileLocalChangesWait()
    {
        var list = _local.Data.CreateList("Groceries");
        var sharedVersion = list with { ChangeVersion = 20, Owner = s_owner, Members = [s_member] };

        Assert.False(_local.Data.StoreServerList(sharedVersion));
        Assert.Null(_local.Data.GetList(list.Id)!.Owner);

        _local.Store.RemoveOperations(EntityTypes.List, list.Id);
        ChangesResponse? raisedChanges = null;
        _local.Data.Changed += (_, changes) => raisedChanges = changes;

        Assert.True(_local.Data.StoreServerList(sharedVersion));
        Assert.Equal([s_member.UserId], _local.Data.GetList(list.Id)!.Members!.Select(member => member.UserId));
        Assert.Equal([list.Id], raisedChanges!.Lists.Select(changedList => changedList.Id));
        Assert.False(_local.Data.StoreServerList(sharedVersion with { ChangeVersion = 19, Members = [] }));
        Assert.Single(_local.Data.GetList(list.Id)!.Members!);
    }

    [Fact]
    public void ForgetList_RemovesTheListItsTasksAndTheirPendingOperations()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 7);
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, [SharedList(6)], [task]), isFullSnapshot: false);
        _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { IsImportant = true });
        var ownTask = _local.Data.CreateTask(new CreateTaskRequest("Call home"));
        ChangesResponse? raisedChanges = null;
        _local.Data.Changed += (_, changes) => raisedChanges = changes;

        _local.Data.ForgetList(s_sharedListId);

        Assert.Null(_local.Data.GetList(s_sharedListId));
        Assert.Null(_local.Data.GetTask(task.Id));
        Assert.NotNull(_local.Data.GetTask(ownTask.Id));
        Assert.Equal([ownTask.Id], _local.Store.GetOperations().Select(operation => operation.EntityId));
        Assert.Equal([s_sharedListId], raisedChanges!.DeletedLists.Select(tombstone => tombstone.Id));
        Assert.Equal([task.Id], raisedChanges.DeletedTasks.Select(tombstone => tombstone.Id));
    }

    [Fact]
    public void ReminderSetOnThisDevice_HasNoRecordedUserUntilTheServerAttributesIt()
    {
        var task = TestData.Task(Guid.CreateVersion7(), s_sharedListId, "Buy milk", 7) with { ReminderAt = TestData.CreatedAt.AddDays(20), ReminderSetByUserId = s_owner.UserId };
        _local.Data.ApplyRemoteChanges(TestData.Changes(8, [SharedList(6)], [task]), isFullSnapshot: false);

        var renamedTask = _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { Title = "Buy oat milk" });
        var remindedTask = _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { ReminderAt = TestData.CreatedAt.AddDays(21) });

        Assert.Equal(s_owner.UserId, renamedTask.ReminderSetByUserId);
        Assert.Null(remindedTask.ReminderSetByUserId);
    }

    private static TaskListResponse SharedList(long changeVersion) => TestData.List(s_sharedListId, "Groceries", changeVersion, sortOrder: 1) with { Owner = s_owner, Members = [s_member] };
}
