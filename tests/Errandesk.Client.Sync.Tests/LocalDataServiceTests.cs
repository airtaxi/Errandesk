using System.Text.Json;
using Errandesk.Client.Sync.Tests.Infrastructure;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests;

public sealed class LocalDataServiceTests : IDisposable
{
    private static readonly Guid s_defaultListId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    private readonly TemporaryDataService _local = new();

    public LocalDataServiceTests() => _local.Data.ApplyRemoteChanges(TestData.Changes(5, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)]), isFullSnapshot: true);

    public void Dispose() => _local.Dispose();

    [Fact]
    public void Views_OrderTasksAsTheServerDoes()
    {
        var first = _local.Data.CreateTask(new CreateTaskRequest("First", DueDate: new DateOnly(2031, 3, 20)));
        var second = _local.Data.CreateTask(new CreateTaskRequest("Second", DueDate: new DateOnly(2031, 3, 12), IsImportant: true));
        var third = _local.Data.CreateTask(new CreateTaskRequest("Third", IsInMyDay: true));
        _local.Data.UpdateTask(first.Id, new UpdateTaskRequest { IsCompleted = true });

        Assert.Equal(["Third", "Second", "First"], _local.Data.GetTasks(s_defaultListId.ToString(), includeCompleted: true).Select(task => task.Title));
        Assert.Equal(["Third", "Second"], _local.Data.GetTasks(s_defaultListId.ToString(), includeCompleted: false).Select(task => task.Title));
        Assert.Equal(["Second", "First"], _local.Data.GetTasks(TaskViews.Planned, includeCompleted: true).Select(task => task.Title));
        Assert.Equal([second.Id], _local.Data.GetTasks(TaskViews.Important, includeCompleted: true).Select(task => task.Id));
        Assert.Equal([third.Id], _local.Data.GetTasks(TaskViews.MyDay, includeCompleted: true).Select(task => task.Id));
        Assert.Equal([-2.0, -1.0, 0.0], [third.SortOrder, second.SortOrder, first.SortOrder]);
    }

    [Fact]
    public void MyDay_ContainsOnlyTasksAddedToday()
    {
        var task = _local.Data.CreateTask(new CreateTaskRequest("Call the bank", IsInMyDay: true));
        Assert.True(_local.Data.GetTask(task.Id)!.IsInMyDay);

        _local.TimeProvider.Advance(TimeSpan.FromDays(1));

        Assert.Empty(_local.Data.GetTasks(TaskViews.MyDay, includeCompleted: true));
        Assert.False(_local.Data.GetTask(task.Id)!.IsInMyDay);
    }

    [Fact]
    public void TaskUpdates_FollowTheServerRules()
    {
        var task = _local.Data.CreateTask(new CreateTaskRequest("Water the plants"));

        var repeating = _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { Recurrence = new TaskRecurrence(RecurrenceFrequency.Weekly, 0, [DayOfWeek.Friday, DayOfWeek.Thursday, DayOfWeek.Friday]) });
        Assert.Equal(new DateOnly(2031, 3, 13), repeating.DueDate);
        Assert.Equal([DayOfWeek.Thursday, DayOfWeek.Friday], repeating.Recurrence!.DaysOfWeek);
        Assert.Equal(1, repeating.Recurrence.Interval);

        var completed = _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { IsCompleted = true, ReminderAt = new DateTimeOffset(2031, 3, 13, 18, 30, 15, 123, TimeSpan.FromHours(9)).AddTicks(42) });
        Assert.True(completed.IsCompleted);
        Assert.Equal(TemporaryDataService.DefaultStartTime, completed.CompletedAt);
        Assert.Equal(new DateTimeOffset(2031, 3, 13, 9, 30, 15, 123, TimeSpan.Zero), completed.ReminderAt);

        var withoutDueDate = _local.Data.UpdateTask(task.Id, new UpdateTaskRequest { DueDate = null, Body = null, IsCompleted = false });
        Assert.Null(withoutDueDate.DueDate);
        Assert.Null(withoutDueDate.Recurrence);
        Assert.Null(withoutDueDate.CompletedAt);
        Assert.Equal("", withoutDueDate.Body);

        var operation = _local.Store.GetOperations()[^1];
        Assert.Equal("PATCH", operation.Method);
        Assert.Equal("""{"body":null,"isCompleted":false,"dueDate":null}""", operation.Body);
    }

    [Fact]
    public void Steps_AreAddedAtTheEndUpdatedAndRemoved()
    {
        var task = _local.Data.CreateTask(new CreateTaskRequest("Pack"));
        _local.Data.AddStep(task.Id, "Passport");
        var withSteps = _local.Data.AddStep(task.Id, " Charger ");
        var charger = withSteps.Steps[1];
        Assert.Equal(["Passport", "Charger"], withSteps.Steps.Select(step => step.Title));
        Assert.Equal(1, charger.SortOrder);

        var updated = _local.Data.UpdateStep(task.Id, charger.Id, new UpdateTaskStepRequest { IsCompleted = true });
        Assert.True(updated.Steps[1].IsCompleted);

        var removed = _local.Data.DeleteStep(task.Id, updated.Steps[0].Id);
        Assert.Equal(["Charger"], removed.Steps.Select(step => step.Title));
        var addRequest = JsonSerializer.Deserialize(_local.Store.GetOperations()[2].Body!, ErrandeskJsonSerializerContext.Default.CreateTaskStepRequest)!;
        Assert.Equal(charger.Id, addRequest.Id);
        Assert.Equal(1, addRequest.SortOrder);
        Assert.Throws<LocalItemNotFoundException>(() => _local.Data.UpdateStep(task.Id, Guid.CreateVersion7(), new UpdateTaskStepRequest { Title = "Missing" }));
    }

    [Fact]
    public void DeletingAList_DeletesItsTasksLocally()
    {
        var list = _local.Data.CreateList(" Groceries ");
        var task = _local.Data.CreateTask(new CreateTaskRequest("Milk", ListId: list.Id));
        ChangesResponse? changes = null;
        _local.Data.Changed += (_, raisedChanges) => changes = raisedChanges;

        _local.Data.DeleteList(list.Id);

        Assert.Equal("Groceries", list.Name);
        Assert.Equal(1, list.SortOrder);
        Assert.Null(_local.Data.GetList(list.Id));
        Assert.Null(_local.Data.GetTask(task.Id));
        Assert.Equal([list.Id], changes!.DeletedLists.Select(tombstone => tombstone.Id));
        Assert.Equal([task.Id], changes.DeletedTasks.Select(tombstone => tombstone.Id));
        Assert.Equal(["POST api/lists", "POST api/tasks", $"DELETE api/lists/{list.Id}"], _local.Store.GetOperations().Select(operation => $"{operation.Method} {operation.Path}"));
    }

    [Fact]
    public void ListChangesFromTheServer_DropTasksOfListsWhoseDeletionIsPending()
    {
        var list = _local.Data.CreateList("Groceries");
        _local.Data.DeleteList(list.Id);

        _local.Data.ApplyRemoteChanges(TestData.Changes(9, tasks: [TestData.Task(Guid.CreateVersion7(), list.Id, "Added elsewhere", 8)]), isFullSnapshot: false);

        Assert.Empty(_local.Data.GetTasks(list.Id.ToString(), includeCompleted: true));
    }

    [Fact]
    public void Notes_AreSearchedFilteredAndCountedByTag()
    {
        _local.Data.CreateNote(new CreateNoteRequest("Trip to Busan", "Visit the 회의록을 beach", Tags: ["#Travel", "Korea", "travel"], IsPinned: false));
        _local.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        _local.Data.CreateNote(new CreateNoteRequest("Recipes", "Kimchi stew", Tags: ["food"]));
        _local.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var pinned = _local.Data.CreateNote(new CreateNoteRequest("Pinned", "", IsPinned: true));

        Assert.Equal(["Pinned", "Recipes", "Trip to Busan"], _local.Data.GetNotes(null, null, null).Select(note => note.Title));
        Assert.Equal(["Trip to Busan"], _local.Data.GetNotes("busan 회의록", null, null).Select(note => note.Title));
        Assert.Equal(["Trip to Busan"], _local.Data.GetNotes("TRAV", null, null).Select(note => note.Title));
        Assert.Equal(["Trip to Busan"], _local.Data.GetNotes(null, "#Travel", null).Select(note => note.Title));
        Assert.Equal([new TagResponse("food", 1), new TagResponse("korea", 1), new TagResponse("travel", 1)], _local.Data.GetTags());
        Assert.True(pinned.IsPinned);
    }

    [Fact]
    public void LinkedNotes_AreShownOnTasksAndChangesReportTheAffectedTasks()
    {
        var task = _local.Data.CreateTask(new CreateTaskRequest("Plan trip"));
        var note = _local.Data.CreateNote(new CreateNoteRequest("Itinerary", "Day 1\n\n  Day 2", LinkedTaskIds: [task.Id, Guid.CreateVersion7()]));
        Assert.Equal([task.Id], note.LinkedTaskIds);
        var summary = Assert.Single(_local.Data.GetTask(task.Id)!.LinkedNotes);
        Assert.Equal("Day 1 Day 2", summary.Excerpt);

        var changedTaskIds = new List<Guid>();
        _local.Data.Changed += (_, changes) => changedTaskIds.AddRange(changes.Tasks.Select(changedTask => changedTask.Id));
        _local.Data.UpdateNote(note.Id, new UpdateNoteRequest { Title = "Final itinerary" });
        Assert.Equal("Final itinerary", Assert.Single(_local.Data.GetTask(task.Id)!.LinkedNotes).Title);

        _local.Data.UnlinkNoteFromTask(note.Id, task.Id);
        Assert.Empty(_local.Data.GetTask(task.Id)!.LinkedNotes);
        _local.Data.LinkNoteToTask(note.Id, task.Id);
        _local.Data.LinkNoteToTask(note.Id, task.Id);
        Assert.Single(_local.Data.GetTask(task.Id)!.LinkedNotes);
        Assert.Equal([note.Id], _local.Data.GetNotes(null, null, task.Id).Select(linkedNote => linkedNote.Id));

        _local.Data.DeleteNote(note.Id);
        Assert.Empty(_local.Data.GetTask(task.Id)!.LinkedNotes);
        Assert.Equal([task.Id, task.Id, task.Id, task.Id], changedTaskIds);
        Assert.Equal(["POST api/tasks", "POST api/notes", $"PATCH api/notes/{note.Id}", $"DELETE api/notes/{note.Id}/linked-tasks/{task.Id}", $"PUT api/notes/{note.Id}/linked-tasks/{task.Id}", $"DELETE api/notes/{note.Id}"], _local.Store.GetOperations().Select(operation => $"{operation.Method} {operation.Path}"));
    }

    [Fact]
    public void ChangesToMissingItems_Throw()
    {
        Assert.Throws<LocalItemNotFoundException>(() => _local.Data.UpdateTask(Guid.CreateVersion7(), new UpdateTaskRequest { Title = "Missing" }));
        Assert.Throws<LocalItemNotFoundException>(() => _local.Data.CreateTask(new CreateTaskRequest("Nowhere", ListId: Guid.CreateVersion7())));
        Assert.Throws<LocalItemNotFoundException>(() => _local.Data.UpdateNote(Guid.CreateVersion7(), new UpdateNoteRequest { Title = "Missing" }));
        Assert.Equal(0, _local.Data.PendingOperationCount);
    }

    [Fact]
    public void Store_KeepsDataAndOutboxAfterReopening()
    {
        var path = Path.Combine(Path.GetTempPath(), "errandesk-sync-tests", $"{Guid.NewGuid():N}.db");
        try
        {
            Guid taskId;
            using (var store = LocalStore.Open(path))
            {
                var data = new LocalDataService(store, TimeProvider.System);
                data.ApplyRemoteChanges(TestData.Changes(3, [TestData.List(s_defaultListId, "Tasks", 1, isDefault: true)]), isFullSnapshot: true);
                taskId = data.CreateTask(new CreateTaskRequest("Survives a restart")).Id;
            }

            using var reopenedStore = LocalStore.Open(path);
            Assert.Equal(3, reopenedStore.Version);
            Assert.Equal("Survives a restart", reopenedStore.GetTask(taskId)?.Title);
            Assert.Equal(taskId, reopenedStore.PeekOperation()?.EntityId);
        }
        finally { LocalStore.Delete(path); }
    }
}
