using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync;

/// <summary>
/// The lists, tasks and notes of the signed-in user as stored on this device. Reads come from the local store;
/// every change is applied to the store first and queued in the outbox for the server.
/// </summary>
/// <remarks>Call members from one thread at a time. <see cref="Changed"/> is raised on the calling thread.</remarks>
public sealed class LocalDataService(LocalStore store, TimeProvider timeProvider)
{
    private const int MaximumSearchWordCount = 10;

    private static readonly char[] s_searchWordSeparators = [' ', '\t', '\r', '\n'];

    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    /// <summary>
    /// Raised after stored data changed, by a change on this device or from the server. Tasks carry their linked note summaries.
    /// <see cref="ChangesResponse.ResetRequired"/> is <see langword="true"/> when the whole data set was replaced, so views should load again.
    /// </summary>
    public event EventHandler<ChangesResponse>? Changed;

    /// <summary>
    /// Raised after a change was added to the outbox.
    /// </summary>
    public event EventHandler? OperationQueued;

    public LocalStore Store => store;

    /// <summary>
    /// Today's date on this device, which decides whether a task is in My Day.
    /// </summary>
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>
    /// Whether data from the server has been stored at least once.
    /// </summary>
    public bool HasSynchronized => store.Version > 0;

    public int PendingOperationCount => store.PendingOperationCount;

    public DateTimeOffset? LastSyncedAt => store.LastSyncedAt;

    private DateTimeOffset Now => LocalEditRules.Truncate(timeProvider.GetUtcNow());

    public IReadOnlyList<TaskListResponse> GetLists() => store.GetLists();

    public TaskListResponse? GetList(Guid listId) => store.GetList(listId);

    public TaskListResponse? GetDefaultList() => store.GetLists().FirstOrDefault(list => list.IsDefault);

    /// <summary>
    /// The tasks of a view, ordered as the server orders them: open tasks by sort order (Planned: by due date first), then completed tasks, most recently completed first.
    /// </summary>
    /// <param name="view"><see cref="TaskViews.MyDay"/>, <see cref="TaskViews.Important"/>, <see cref="TaskViews.Planned"/> or a list ID.</param>
    /// <param name="includeCompleted">Whether completed tasks follow the open ones.</param>
    public IReadOnlyList<TaskItemResponse> GetTasks(string view, bool includeCompleted)
    {
        var today = Today;
        IReadOnlyList<TaskItemResponse> tasks = view.ToLowerInvariant() switch
        {
            TaskViews.MyDay => store.GetTasksInMyDay(today),
            TaskViews.Important => store.GetImportantTasks(),
            TaskViews.Planned => store.GetPlannedTasks(),
            _ => Guid.TryParse(view, out var listId) ? store.GetTasksInList(listId) : throw new ArgumentException($"The view {view} is unknown.", nameof(view)),
        };

        var isPlanned = string.Equals(view, TaskViews.Planned, StringComparison.OrdinalIgnoreCase);
        var openTasks = tasks.Where(task => !task.IsCompleted).OrderBy(task => isPlanned ? task.DueDate : null).ThenBy(task => task.SortOrder).ThenBy(task => task.CreatedAt).ThenBy(task => task.Id);
        var completedTasks = includeCompleted ? tasks.Where(task => task.IsCompleted).OrderByDescending(task => task.CompletedAt).ThenBy(task => task.Id) : Enumerable.Empty<TaskItemResponse>();
        return Complete([.. openTasks, .. completedTasks], today);
    }

    public TaskItemResponse? GetTask(Guid taskId) => store.GetTask(taskId) is { } task ? Complete([task], Today)[0] : null;

    /// <summary>
    /// Open tasks that have a reminder, which the reminder schedulers show.
    /// </summary>
    public IReadOnlyList<TaskItemResponse> GetOpenTasksWithReminders() => store.GetOpenTasksWithReminders();

    /// <summary>
    /// The notes that match every given filter, pinned notes first, then the most recently changed.
    /// </summary>
    /// <param name="query">Words that must each appear in the title, the content or a tag, ignoring case.</param>
    /// <param name="tag">A tag the notes must carry.</param>
    /// <param name="linkedTaskId">A task the notes must be linked to.</param>
    public IReadOnlyList<NoteResponse> GetNotes(string? query, string? tag, Guid? linkedTaskId)
    {
        var words = (query ?? "").Split(s_searchWordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaximumSearchWordCount).ToArray();
        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : LocalEditRules.NormalizeTag(tag);
        return [.. store.GetNotes().Where(note => (normalizedTag is null || note.Tags.Contains(normalizedTag)) && (linkedTaskId is not { } taskId || note.LinkedTaskIds.Contains(taskId)) && words.All(word => Contains(note, word)))];
    }

    public NoteResponse? GetNote(Guid noteId) => store.GetNote(noteId);

    /// <summary>
    /// Every tag of the stored notes with the number of notes that carry it, ordered by name.
    /// </summary>
    public IReadOnlyList<TagResponse> GetTags() =>
    [.. store.GetNotes().SelectMany(note => note.Tags).GroupBy(tag => tag, StringComparer.Ordinal).Select(group => new TagResponse(group.Key, group.Count())).OrderBy(tag => tag.Name, StringComparer.Ordinal)];

    public TaskListResponse CreateList(string name)
    {
        var list = LocalEditRules.CreateList(Guid.CreateVersion7(), name, store.GetLists(), Now);
        var request = new CreateTaskListRequest(list.Name, list.Id, list.SortOrder);
        Commit(() =>
        {
            store.UpsertList(list);
            Enqueue(EntityTypes.List, list.Id, HttpMethod.Post, "api/lists", request, JsonContext.CreateTaskListRequest);
        });
        RaiseChanged(lists: [list]);
        return list;
    }

    /// <exception cref="LocalItemNotFoundException">The list is not stored.</exception>
    public TaskListResponse UpdateList(Guid listId, UpdateTaskListRequest request)
    {
        var list = LocalEditRules.ApplyListUpdate(store.GetList(listId) ?? throw new LocalItemNotFoundException(EntityTypes.List, listId), request, Now);
        Commit(() =>
        {
            store.UpsertList(list);
            Enqueue(EntityTypes.List, listId, HttpMethod.Patch, $"api/lists/{listId}", request, JsonContext.UpdateTaskListRequest);
        });
        RaiseChanged(lists: [list]);
        return list;
    }

    /// <summary>
    /// Deletes a list together with its tasks.
    /// </summary>
    /// <exception cref="LocalItemNotFoundException">The list is not stored.</exception>
    public void DeleteList(Guid listId)
    {
        if (store.GetList(listId) is null) throw new LocalItemNotFoundException(EntityTypes.List, listId);

        var taskIds = store.GetTaskIdsInList(listId);
        Commit(() =>
        {
            store.DeleteList(listId);
            foreach (var taskId in taskIds) store.DeleteTask(taskId);
            Enqueue<object>(EntityTypes.List, listId, HttpMethod.Delete, $"api/lists/{listId}", null, null);
        });
        RaiseChanged(deletedListIds: [listId], deletedTaskIds: taskIds);
    }

    /// <summary>
    /// Creates a task. Without a list ID the task goes to the default list; without a sort order it goes to the top of its list.
    /// </summary>
    /// <exception cref="LocalItemNotFoundException">The list is not stored.</exception>
    public TaskItemResponse CreateTask(CreateTaskRequest request)
    {
        var taskId = request.Id ?? Guid.CreateVersion7();
        var listId = request.ListId ?? GetDefaultList()?.Id ?? throw new LocalItemNotFoundException(EntityTypes.List, Guid.Empty);
        if (store.GetList(listId) is null) throw new LocalItemNotFoundException(EntityTypes.List, listId);

        var task = LocalEditRules.CreateTask(request, taskId, listId, store.GetTasksInList(listId), Today, Now);
        var sentRequest = request with
        {
            Id = taskId,
            ListId = listId,
            SortOrder = task.SortOrder,
            Steps = task.Steps.Count == 0 ? null : [.. task.Steps.Select(step => new CreateTaskStepRequest(step.Title, step.Id, step.IsCompleted, step.SortOrder))],
        };
        Commit(() =>
        {
            store.UpsertTask(task);
            Enqueue(EntityTypes.Task, taskId, HttpMethod.Post, "api/tasks", sentRequest, JsonContext.CreateTaskRequest);
        });
        return RaiseTaskChanged(taskId);
    }

    /// <exception cref="LocalItemNotFoundException">The task, or the list it moves to, is not stored.</exception>
    public TaskItemResponse UpdateTask(Guid taskId, UpdateTaskRequest request)
    {
        var task = store.GetTask(taskId) ?? throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        if (request.ListId.IsSpecified && store.GetList(request.ListId.Value) is null) throw new LocalItemNotFoundException(EntityTypes.List, request.ListId.Value);

        var updatedTask = LocalEditRules.ApplyTaskUpdate(task, request, Today, Now);
        Commit(() =>
        {
            store.UpsertTask(updatedTask);
            Enqueue(EntityTypes.Task, taskId, HttpMethod.Patch, $"api/tasks/{taskId}", request, JsonContext.UpdateTaskRequest);
        });
        return RaiseTaskChanged(taskId);
    }

    /// <exception cref="LocalItemNotFoundException">The task is not stored.</exception>
    public void DeleteTask(Guid taskId)
    {
        if (store.GetTask(taskId) is null) throw new LocalItemNotFoundException(EntityTypes.Task, taskId);

        Commit(() =>
        {
            store.DeleteTask(taskId);
            Enqueue<object>(EntityTypes.Task, taskId, HttpMethod.Delete, $"api/tasks/{taskId}", null, null);
        });
        RaiseChanged(deletedTaskIds: [taskId]);
    }

    /// <summary>
    /// Adds a step after the last step of a task.
    /// </summary>
    /// <exception cref="LocalItemNotFoundException">The task is not stored.</exception>
    public TaskItemResponse AddStep(Guid taskId, string title)
    {
        var task = store.GetTask(taskId) ?? throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        var stepId = Guid.CreateVersion7();
        var updatedTask = LocalEditRules.AddStep(task, new CreateTaskStepRequest(title), stepId, Now);
        var addedStep = updatedTask.Steps.First(step => step.Id == stepId);
        var request = new CreateTaskStepRequest(addedStep.Title, stepId, addedStep.IsCompleted, addedStep.SortOrder);
        Commit(() =>
        {
            store.UpsertTask(updatedTask);
            Enqueue(EntityTypes.Task, taskId, HttpMethod.Post, $"api/tasks/{taskId}/steps", request, JsonContext.CreateTaskStepRequest);
        });
        return RaiseTaskChanged(taskId);
    }

    /// <exception cref="LocalItemNotFoundException">The task or the step is not stored.</exception>
    public TaskItemResponse UpdateStep(Guid taskId, Guid stepId, UpdateTaskStepRequest request)
    {
        var task = store.GetTask(taskId) ?? throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        var updatedTask = LocalEditRules.ApplyStepUpdate(task, stepId, request, Now);
        Commit(() =>
        {
            store.UpsertTask(updatedTask);
            Enqueue(EntityTypes.Task, taskId, HttpMethod.Patch, $"api/tasks/{taskId}/steps/{stepId}", request, JsonContext.UpdateTaskStepRequest);
        });
        return RaiseTaskChanged(taskId);
    }

    /// <exception cref="LocalItemNotFoundException">The task is not stored.</exception>
    public TaskItemResponse DeleteStep(Guid taskId, Guid stepId)
    {
        var task = store.GetTask(taskId) ?? throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        var updatedTask = LocalEditRules.RemoveStep(task, stepId, Now);
        Commit(() =>
        {
            store.UpsertTask(updatedTask);
            Enqueue<object>(EntityTypes.Task, taskId, HttpMethod.Delete, $"api/tasks/{taskId}/steps/{stepId}", null, null);
        });
        return RaiseTaskChanged(taskId);
    }

    /// <summary>
    /// Creates a note. Links to tasks that are not stored are left out.
    /// </summary>
    public NoteResponse CreateNote(CreateNoteRequest request)
    {
        var noteId = request.Id ?? Guid.CreateVersion7();
        var linkedTaskIds = (request.LinkedTaskIds ?? []).Where(taskId => store.GetTask(taskId) is not null);
        var note = LocalEditRules.CreateNote(request, noteId, linkedTaskIds, Now);
        var sentRequest = request with { Title = note.Title, Id = noteId, Tags = note.Tags, LinkedTaskIds = note.LinkedTaskIds.Count == 0 ? null : note.LinkedTaskIds };
        Commit(() =>
        {
            store.UpsertNote(note);
            Enqueue(EntityTypes.Note, noteId, HttpMethod.Post, "api/notes", sentRequest, JsonContext.CreateNoteRequest);
        });
        RaiseChanged(notes: [note], taskIds: note.LinkedTaskIds);
        return note;
    }

    /// <exception cref="LocalItemNotFoundException">The note is not stored.</exception>
    public NoteResponse UpdateNote(Guid noteId, UpdateNoteRequest request)
    {
        var note = LocalEditRules.ApplyNoteUpdate(store.GetNote(noteId) ?? throw new LocalItemNotFoundException(EntityTypes.Note, noteId), request, Now);
        Commit(() =>
        {
            store.UpsertNote(note);
            Enqueue(EntityTypes.Note, noteId, HttpMethod.Patch, $"api/notes/{noteId}", request, JsonContext.UpdateNoteRequest);
        });
        RaiseChanged(notes: [note], taskIds: note.LinkedTaskIds);
        return note;
    }

    /// <exception cref="LocalItemNotFoundException">The note is not stored.</exception>
    public void DeleteNote(Guid noteId)
    {
        var note = store.GetNote(noteId) ?? throw new LocalItemNotFoundException(EntityTypes.Note, noteId);
        Commit(() =>
        {
            store.DeleteNote(noteId);
            Enqueue<object>(EntityTypes.Note, noteId, HttpMethod.Delete, $"api/notes/{noteId}", null, null);
        });
        RaiseChanged(deletedNoteIds: [noteId], taskIds: note.LinkedTaskIds);
    }

    /// <summary>
    /// Links a note to a task. Linking an already linked task changes nothing.
    /// </summary>
    /// <exception cref="LocalItemNotFoundException">The note or the task is not stored.</exception>
    public NoteResponse LinkNoteToTask(Guid noteId, Guid taskId)
    {
        var note = store.GetNote(noteId) ?? throw new LocalItemNotFoundException(EntityTypes.Note, noteId);
        if (store.GetTask(taskId) is null) throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        if (note.LinkedTaskIds.Contains(taskId)) return note;

        var linkedNote = LocalEditRules.LinkTask(note, taskId, Now);
        Commit(() =>
        {
            store.UpsertNote(linkedNote);
            Enqueue<object>(EntityTypes.Note, noteId, HttpMethod.Put, $"api/notes/{noteId}/linked-tasks/{taskId}", null, null);
        });
        RaiseChanged(notes: [linkedNote], taskIds: [taskId]);
        return linkedNote;
    }

    /// <summary>
    /// Removes the link between a note and a task. A task that is not linked changes nothing.
    /// </summary>
    /// <exception cref="LocalItemNotFoundException">The note is not stored.</exception>
    public NoteResponse UnlinkNoteFromTask(Guid noteId, Guid taskId)
    {
        var note = store.GetNote(noteId) ?? throw new LocalItemNotFoundException(EntityTypes.Note, noteId);
        if (!note.LinkedTaskIds.Contains(taskId)) return note;

        var unlinkedNote = LocalEditRules.UnlinkTask(note, taskId, Now);
        Commit(() =>
        {
            store.UpsertNote(unlinkedNote);
            Enqueue<object>(EntityTypes.Note, noteId, HttpMethod.Delete, $"api/notes/{noteId}/linked-tasks/{taskId}", null, null);
        });
        RaiseChanged(notes: [unlinkedNote], taskIds: [taskId]);
        return unlinkedNote;
    }

    /// <summary>
    /// Stores a list the server returned outside the outbox, for example after its members changed.
    /// A list with pending local operations, or a response older than the stored list, is left alone; the change feed brings the server state later.
    /// </summary>
    /// <returns>Whether the list was stored.</returns>
    public bool StoreServerList(TaskListResponse list)
    {
        var isStored = false;
        store.RunInTransaction(() =>
        {
            if (store.HasPendingOperations(EntityTypes.List, list.Id) || !IsNewer(EntityTypes.List, list.Id, list.ChangeVersion)) return;
            store.UpsertList(list);
            isStored = true;
        });

        if (isStored) RaiseChanged(lists: [list]);
        return isStored;
    }

    /// <summary>
    /// Removes a list the user can no longer see, for example after leaving it, together with its tasks and their pending operations, without telling the server.
    /// </summary>
    public void ForgetList(Guid listId)
    {
        List<Guid> deletedTaskIds = [];
        var isListDeleted = false;
        store.RunInTransaction(() =>
        {
            foreach (var taskId in store.GetTaskIdsInList(listId))
            {
                store.RemoveOperations(EntityTypes.Task, taskId);
                store.RemoveRefresh(EntityTypes.Task, taskId);
                if (store.DeleteTask(taskId)) deletedTaskIds.Add(taskId);
            }

            store.RemoveOperations(EntityTypes.List, listId);
            store.RemoveRefresh(EntityTypes.List, listId);
            isListDeleted = store.DeleteList(listId);
        });

        if (isListDeleted || deletedTaskIds.Count > 0) RaiseChanged(deletedListIds: isListDeleted ? [listId] : null, deletedTaskIds: deletedTaskIds);
    }

    /// <summary>
    /// Applies changes pulled from the server. Items with pending local operations keep their local state and are read again after the operations were sent.
    /// A deleted list also removes its stored tasks, because losing access to a shared list arrives as list and task tombstones.
    /// </summary>
    /// <param name="changes">The change feed response.</param>
    /// <param name="isFullSnapshot">Whether <paramref name="changes"/> holds every item of the user, so stored items missing from it are removed.</param>
    internal void ApplyRemoteChanges(ChangesResponse changes, bool isFullSnapshot)
    {
        var pendingListIds = store.GetPendingEntityIds(EntityTypes.List);
        var pendingTaskIds = store.GetPendingEntityIds(EntityTypes.Task);
        var pendingNoteIds = store.GetPendingEntityIds(EntityTypes.Note);
        var listIdsPendingDeletion = store.GetListIdsPendingDeletion();
        List<TaskListResponse> appliedLists = [];
        List<NoteResponse> appliedNotes = [];
        HashSet<Guid> changedTaskIds = [];
        List<Guid> deletedListIds = [];
        List<Guid> deletedTaskIds = [];
        List<Guid> deletedNoteIds = [];

        store.RunInTransaction(() =>
        {
            if (isFullSnapshot)
            {
                deletedListIds.AddRange(RemoveMissing(EntityTypes.List, changes.Lists.Select(list => list.Id), pendingListIds, store.DeleteList));
                deletedTaskIds.AddRange(RemoveMissing(EntityTypes.Task, changes.Tasks.Select(task => task.Id), pendingTaskIds, store.DeleteTask));
                foreach (var noteId in store.GetIds(EntityTypes.Note).Except(changes.Notes.Select(note => note.Id)).Where(noteId => !pendingNoteIds.Contains(noteId)).ToList()) DeleteRemoteNote(noteId, deletedNoteIds, changedTaskIds);
            }

            foreach (var tombstone in changes.DeletedLists)
            {
                if (pendingListIds.Contains(tombstone.Id))
                {
                    store.QueueRefresh(EntityTypes.List, tombstone.Id);
                    continue;
                }

                if (store.DeleteList(tombstone.Id)) deletedListIds.Add(tombstone.Id);
                foreach (var taskId in store.GetTaskIdsInList(tombstone.Id))
                {
                    if (pendingTaskIds.Contains(taskId)) store.QueueRefresh(EntityTypes.Task, taskId);
                    else if (store.DeleteTask(taskId)) deletedTaskIds.Add(taskId);
                }
            }

            foreach (var list in changes.Lists)
            {
                if (pendingListIds.Contains(list.Id)) store.QueueRefresh(EntityTypes.List, list.Id);
                else if (IsNewer(EntityTypes.List, list.Id, list.ChangeVersion))
                {
                    store.UpsertList(list);
                    appliedLists.Add(list);
                }
            }

            foreach (var tombstone in changes.DeletedTasks)
            {
                if (pendingTaskIds.Contains(tombstone.Id)) store.QueueRefresh(EntityTypes.Task, tombstone.Id);
                else if (store.DeleteTask(tombstone.Id)) deletedTaskIds.Add(tombstone.Id);
            }

            foreach (var task in changes.Tasks)
            {
                if (pendingTaskIds.Contains(task.Id)) store.QueueRefresh(EntityTypes.Task, task.Id);
                else if (!listIdsPendingDeletion.Contains(task.ListId) && IsNewer(EntityTypes.Task, task.Id, task.ChangeVersion))
                {
                    store.UpsertTask(task);
                    changedTaskIds.Add(task.Id);
                }
            }

            foreach (var tombstone in changes.DeletedNotes)
            {
                if (pendingNoteIds.Contains(tombstone.Id)) store.QueueRefresh(EntityTypes.Note, tombstone.Id);
                else DeleteRemoteNote(tombstone.Id, deletedNoteIds, changedTaskIds);
            }

            foreach (var note in changes.Notes)
            {
                if (pendingNoteIds.Contains(note.Id)) store.QueueRefresh(EntityTypes.Note, note.Id);
                else if (IsNewer(EntityTypes.Note, note.Id, note.ChangeVersion))
                {
                    changedTaskIds.UnionWith(store.GetLinkedTaskIds(note.Id));
                    store.UpsertNote(note);
                    changedTaskIds.UnionWith(note.LinkedTaskIds);
                    appliedNotes.Add(note);
                }
            }

            store.Version = isFullSnapshot ? changes.CurrentVersion : Math.Max(store.Version, changes.CurrentVersion);
        });

        changedTaskIds.ExceptWith(deletedTaskIds);
        if (isFullSnapshot || appliedLists.Count > 0 || appliedNotes.Count > 0 || changedTaskIds.Count > 0 || deletedListIds.Count > 0 || deletedTaskIds.Count > 0 || deletedNoteIds.Count > 0) RaiseChanged(appliedLists, changedTaskIds, appliedNotes, deletedListIds, deletedTaskIds, deletedNoteIds, isFullSnapshot);
    }

    /// <summary>
    /// Removes an operation the server accepted and stores the server's version of the item when no later local change waits for it.
    /// </summary>
    internal void AcknowledgeOperation(OutboxOperation operation, string? responseBody)
    {
        object? appliedItem = null;
        IReadOnlyList<Guid> previousLinkedTaskIds = [];
        store.RunInTransaction(() =>
        {
            store.RemoveOperation(operation.Sequence);
            if (responseBody is null || store.HasPendingOperations(operation.EntityType, operation.EntityId) || store.GetChangeVersion(operation.EntityType, operation.EntityId) is not { } storedVersion) return;
            if (operation.EntityType == EntityTypes.Note) previousLinkedTaskIds = store.GetLinkedTaskIds(operation.EntityId);
            appliedItem = StoreItem(operation.EntityType, operation.EntityId, responseBody, minimumChangeVersion: storedVersion);
        });

        if (appliedItem is not null) RaiseItemChanged(appliedItem, previousLinkedTaskIds);
    }

    /// <summary>
    /// Removes an operation the server refused and marks its item to be read again from the server.
    /// </summary>
    internal void RejectOperation(OutboxOperation operation)
    {
        store.RunInTransaction(() =>
        {
            store.RemoveOperation(operation.Sequence);
            store.QueueRefresh(operation.EntityType, operation.EntityId);
        });
    }

    internal void RecordOperationFailure(OutboxOperation operation, string error) => store.RecordFailure(operation.Sequence, error);

    /// <summary>
    /// Stores the server's state of an item that was read again: the item itself, or its removal when the server answered 404.
    /// </summary>
    /// <returns><see langword="false"/> when the response is neither the item nor 404, so the item stays queued.</returns>
    internal bool ApplyRefreshedItem(RefreshItem item, SyncTransportResponse response)
    {
        if (response.StatusCode != 404 && (!response.IsSuccess || response.Body is null)) return false;

        object? appliedItem = null;
        IReadOnlyList<Guid> previousLinkedTaskIds = [];
        var isDeleted = false;
        store.RunInTransaction(() =>
        {
            store.RemoveRefresh(item.EntityType, item.EntityId);
            if (store.HasPendingOperations(item.EntityType, item.EntityId)) return;
            if (item.EntityType == EntityTypes.Note) previousLinkedTaskIds = store.GetLinkedTaskIds(item.EntityId);
            if (response.StatusCode == 404) isDeleted = DeleteItem(item.EntityType, item.EntityId);
            else appliedItem = StoreItem(item.EntityType, item.EntityId, response.Body!, minimumChangeVersion: null);
        });

        if (appliedItem is not null) RaiseItemChanged(appliedItem, previousLinkedTaskIds);
        else if (isDeleted) RaiseChanged(taskIds: previousLinkedTaskIds, deletedListIds: item.EntityType == EntityTypes.List ? [item.EntityId] : null, deletedTaskIds: item.EntityType == EntityTypes.Task ? [item.EntityId] : null, deletedNoteIds: item.EntityType == EntityTypes.Note ? [item.EntityId] : null);

        return true;
    }

    internal void MarkSynchronized() => store.LastSyncedAt = timeProvider.GetUtcNow();

    private static bool Contains(NoteResponse note, string word) => note.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase) || note.Content.Contains(word, StringComparison.CurrentCultureIgnoreCase) || note.Tags.Any(tag => tag.Contains(word, StringComparison.CurrentCultureIgnoreCase));

    private bool IsNewer(string entityType, Guid id, long changeVersion) => store.GetChangeVersion(entityType, id) is not { } storedVersion || changeVersion > storedVersion;

    private List<Guid> RemoveMissing(string entityType, IEnumerable<Guid> snapshotIds, IReadOnlySet<Guid> pendingIds, Func<Guid, bool> delete)
    {
        var missingIds = store.GetIds(entityType).Except(snapshotIds).Where(id => !pendingIds.Contains(id)).ToList();
        foreach (var id in missingIds) delete(id);
        return missingIds;
    }

    private void DeleteRemoteNote(Guid noteId, List<Guid> deletedNoteIds, HashSet<Guid> changedTaskIds)
    {
        var linkedTaskIds = store.GetLinkedTaskIds(noteId);
        if (!store.DeleteNote(noteId)) return;
        deletedNoteIds.Add(noteId);
        changedTaskIds.UnionWith(linkedTaskIds);
    }

    /// <summary>
    /// Stores an item from a response body when its change version is not older than <paramref name="minimumChangeVersion"/>.
    /// </summary>
    /// <returns>The stored item, or <see langword="null"/> when it was not stored.</returns>
    private object? StoreItem(string entityType, Guid id, string body, long? minimumChangeVersion)
    {
        try
        {
            switch (entityType)
            {
                case EntityTypes.List when JsonSerializer.Deserialize(body, JsonContext.TaskListResponse) is { } list && list.Id == id && list.ChangeVersion >= (minimumChangeVersion ?? 0):
                    store.UpsertList(list);
                    return list;
                case EntityTypes.Task when JsonSerializer.Deserialize(body, JsonContext.TaskItemResponse) is { } task && task.Id == id && task.ChangeVersion >= (minimumChangeVersion ?? 0):
                    store.UpsertTask(task);
                    return task;
                case EntityTypes.Note when JsonSerializer.Deserialize(body, JsonContext.NoteResponse) is { } note && note.Id == id && note.ChangeVersion >= (minimumChangeVersion ?? 0):
                    store.UpsertNote(note);
                    return note;
                default:
                    return null;
            }
        }
        catch (JsonException) { return null; }
    }

    private bool DeleteItem(string entityType, Guid id)
    {
        switch (entityType)
        {
            case EntityTypes.List:
                foreach (var taskId in store.GetTaskIdsInList(id)) store.DeleteTask(taskId);
                return store.DeleteList(id);
            case EntityTypes.Task:
                return store.DeleteTask(id);
            case EntityTypes.Note:
                return store.DeleteNote(id);
            default:
                return false;
        }
    }

    private void RaiseItemChanged(object item, IReadOnlyList<Guid> previousLinkedTaskIds)
    {
        switch (item)
        {
            case TaskListResponse list:
                RaiseChanged(lists: [list]);
                break;
            case TaskItemResponse task:
                RaiseChanged(taskIds: [task.Id]);
                break;
            case NoteResponse note:
                RaiseChanged(notes: [note], taskIds: [.. previousLinkedTaskIds, .. note.LinkedTaskIds]);
                break;
        }
    }

    private TaskItemResponse RaiseTaskChanged(Guid taskId)
    {
        var task = GetTask(taskId) ?? throw new LocalItemNotFoundException(EntityTypes.Task, taskId);
        Changed?.Invoke(this, CreateChanges(tasks: [task]));
        return task;
    }

    private void RaiseChanged(IReadOnlyList<TaskListResponse>? lists = null, IEnumerable<Guid>? taskIds = null, IReadOnlyList<NoteResponse>? notes = null, IReadOnlyList<Guid>? deletedListIds = null, IReadOnlyList<Guid>? deletedTaskIds = null, IReadOnlyList<Guid>? deletedNoteIds = null, bool isFullSnapshot = false)
    {
        var today = Today;
        List<TaskItemResponse> storedTasks = [];
        foreach (var taskId in (taskIds ?? []).Distinct())
        {
            if (store.GetTask(taskId) is { } task)
            {
                storedTasks.Add(task);
            }
        }

        Changed?.Invoke(this, CreateChanges(lists, Complete(storedTasks, today), notes, deletedListIds, deletedTaskIds, deletedNoteIds, isFullSnapshot));
    }

    private ChangesResponse CreateChanges(IReadOnlyList<TaskListResponse>? lists = null, IReadOnlyList<TaskItemResponse>? tasks = null, IReadOnlyList<NoteResponse>? notes = null, IReadOnlyList<Guid>? deletedListIds = null, IReadOnlyList<Guid>? deletedTaskIds = null, IReadOnlyList<Guid>? deletedNoteIds = null, bool isFullSnapshot = false)
    {
        var now = Now;
        return new ChangesResponse(store.Version, lists ?? [], tasks ?? [], notes ?? [], ToTombstones(deletedListIds, now), ToTombstones(deletedTaskIds, now), ToTombstones(deletedNoteIds, now), isFullSnapshot);
    }

    private static TombstoneResponse[] ToTombstones(IReadOnlyList<Guid>? ids, DateTimeOffset deletedAt) =>
    [.. (ids ?? []).Select(id => new TombstoneResponse(id, 0, deletedAt))];

    /// <summary>
    /// Adds the linked note summaries and whether each task is in today's My Day.
    /// </summary>
    private List<TaskItemResponse> Complete(IReadOnlyList<TaskItemResponse> tasks, DateOnly today)
    {
        var linkedNotes = store.GetLinkedNoteSummaries([.. tasks.Select(task => task.Id)]);
        return [.. tasks.Select(task => task with { IsInMyDay = task.MyDayDate == today, LinkedNotes = linkedNotes.TryGetValue(task.Id, out var summaries) ? summaries : [] })];
    }

    private void Commit(Action action)
    {
        store.RunInTransaction(action);
        OperationQueued?.Invoke(this, EventArgs.Empty);
    }

    private void Enqueue<TRequest>(string entityType, Guid entityId, HttpMethod method, string path, TRequest? request, JsonTypeInfo<TRequest>? typeInfo) => store.Enqueue(entityType, entityId, method.Method, path, request is null || typeInfo is null ? null : JsonSerializer.Serialize(request, typeInfo), Now);
}
