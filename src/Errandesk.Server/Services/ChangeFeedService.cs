using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Reads the lists, tasks and notes a user can see that changed after a change version, including tombstones of deleted items.
/// Lists include those shared with the user. When the user gains access to a list, every task of that list is returned; when the user loses access to a list,
/// or a task moves to a list the user cannot see, the list and its tasks, or the task, are returned as tombstones so the client drops them.
/// </summary>
/// <remarks>
/// Change versions are taken before a write commits, so a write can become visible after a later version was already read.
/// Documents changed within <see cref="LateCommitWindow"/> are therefore returned again even when their version is not newer than <c>since</c>.
/// When tombstones newer than <c>since</c> were purged, the response holds every current document and sets <see cref="ChangesResponse.ResetRequired"/>.
/// </remarks>
public sealed class ChangeFeedService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, UserService userService, TaskListService taskListService, TaskListAccess taskListAccess, NoteService noteService, DocumentUpdater documentUpdater, TombstonePurger tombstonePurger)
{
    /// <summary>How long recently changed documents are repeated in later responses.</summary>
    public static readonly TimeSpan LateCommitWindow = TimeSpan.FromSeconds(30);

    public async Task<ChangesResponse> GetChangesAsync(Guid userId, long since, CancellationToken cancellationToken)
    {
        if (since < 0) throw new RequestValidationException("The since version must not be negative.");

        var changes = await ReadChangesAsync(userId, since, cancellationToken);

        // The purge raises the purged-through version before it removes tombstones, so reading it after the documents detects every removal the read could have missed.
        var purgedThroughVersion = await tombstonePurger.GetPurgedThroughVersionAsync(cancellationToken);
        return since > 0 && since < purgedThroughVersion ? await ReadChangesAsync(userId, 0, cancellationToken) with { ResetRequired = true } : changes;
    }

    private async Task<ChangesResponse> ReadChangesAsync(Guid userId, long since, CancellationToken cancellationToken)
    {
        var currentVersion = await changeVersionGenerator.GetCurrentAsync(cancellationToken);
        var recentThreshold = documentUpdater.GetUtcNow() - LateCommitWindow;
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        bool IsChanged(DocumentBase document) => document.ChangeVersion > since || document.UpdatedAt >= recentThreshold;

        var accessibleLists = await database.TaskLists.Find(TaskListAccess.AccessibleListsFilter(userId)).ToListAsync(cancellationToken);
        var accessibleListIds = accessibleLists.Select(list => list.Id).ToList();
        var changedLists = accessibleLists.Where(IsChanged).OrderBy(list => list.ChangeVersion).ToList();
        var grantedListIds = accessibleLists.Where(list => !list.IsDeleted && list.Members.Exists(member => member.UserId == userId && (member.ChangeVersion > since || member.AddedAt >= recentThreshold))).Select(list => list.Id).ToList();

        var taskFilterBuilder = Builders<TaskDocument>.Filter;
        var grantedTasksFilter = taskFilterBuilder.In(task => task.ListId, grantedListIds) & taskFilterBuilder.Eq(task => task.IsDeleted, false);
        var tasks = await database.Tasks.Find(taskFilterBuilder.In(task => task.ListId, accessibleListIds) & (ChangedFilter<TaskDocument>(since, recentThreshold) | grantedTasksFilter)).ToListAsync(cancellationToken);
        var notes = await database.Notes.Find(Builders<NoteDocument>.Filter.Eq(note => note.OwnerUserId, userId) & ChangedFilter<NoteDocument>(since, recentThreshold)).ToListAsync(cancellationToken);

        var liveTasks = tasks.Where(task => !task.IsDeleted).OrderBy(task => task.ChangeVersion).ToList();
        var linkedNotes = await noteService.GetLinkedNoteSummariesAsync(userId, [.. liveTasks.Select(task => task.Id)], cancellationToken);
        var liveNotes = notes.Where(note => !note.IsDeleted).OrderBy(note => note.ChangeVersion).ToList();
        var visibleTaskIds = await GetVisibleTaskIdsAsync(userId, liveNotes.SelectMany(note => note.LinkedTaskIds), cancellationToken);

        var deletedLists = ToTombstones(changedLists);
        var deletedTasks = ToTombstones(tasks);
        if (since > 0) await AddRevocationsAsync(userId, since, recentThreshold, accessibleListIds, deletedLists, deletedTasks, cancellationToken);

        var listResponses = await taskListService.ToResponsesAsync([.. changedLists.Where(list => !list.IsDeleted)], cancellationToken);
        return new ChangesResponse(currentVersion, listResponses, [.. liveTasks.Select(task => task.ToResponse(userId, today, linkedNotes.GetValueOrDefault(task.Id) ?? []))], [.. liveNotes.Select(note => note.ToResponse(visibleTaskIds))], deletedLists, deletedTasks, ToTombstones(notes));
    }

    /// <summary>
    /// Adds tombstones for what the user lost access to after <paramref name="since"/>: lists they were removed from or left, with every task in them,
    /// and tasks that moved into a list they cannot see.
    /// </summary>
    private async Task AddRevocationsAsync(Guid userId, long since, DateTime recentThreshold, List<Guid> accessibleListIds, List<TombstoneResponse> deletedLists, List<TombstoneResponse> deletedTasks, CancellationToken cancellationToken)
    {
        var listedListIds = deletedLists.Select(tombstone => tombstone.Id).ToHashSet();
        var listedTaskIds = deletedTasks.Select(tombstone => tombstone.Id).ToHashSet();
        void AddTask(Guid taskId, long changeVersion, DateTime changedAt)
        {
            if (listedTaskIds.Add(taskId))
            {
                deletedTasks.Add(new TombstoneResponse(taskId, changeVersion, ResponseMappings.ToUtcOffset(changedAt)));
            }
        }

        var formerMemberFilter = Builders<TaskListDocument>.Filter.AnyEq(list => list.FormerMemberUserIds, userId) & ChangedFilter<TaskListDocument>(since, recentThreshold);
        var revokedLists = (await database.TaskLists.Find(formerMemberFilter).ToListAsync(cancellationToken)).Where(list => !list.CanAccess(userId)).OrderBy(list => list.ChangeVersion);
        foreach (var list in revokedLists)
        {
            if (listedListIds.Add(list.Id)) deletedLists.Add(new TombstoneResponse(list.Id, list.ChangeVersion, ResponseMappings.ToUtcOffset(list.UpdatedAt)));
            var taskIds = await database.Tasks.Find(Builders<TaskDocument>.Filter.Eq(task => task.ListId, list.Id)).Project(task => task.Id).ToListAsync(cancellationToken);
            foreach (var taskId in taskIds) AddTask(taskId, list.ChangeVersion, list.UpdatedAt);
        }

        var taskFilterBuilder = Builders<TaskDocument>.Filter;
        var movedAwayFilter = taskFilterBuilder.AnyEq(task => task.RevokedUserIds, userId) & taskFilterBuilder.Nin(task => task.ListId, accessibleListIds) & ChangedFilter<TaskDocument>(since, recentThreshold);
        var movedAwayTasks = await database.Tasks.Find(movedAwayFilter).SortBy(task => task.ChangeVersion).ToListAsync(cancellationToken);
        foreach (var task in movedAwayTasks) AddTask(task.Id, task.ChangeVersion, task.UpdatedAt);
    }

    private async Task<HashSet<Guid>> GetVisibleTaskIdsAsync(Guid userId, IEnumerable<Guid> taskIds, CancellationToken cancellationToken)
    {
        var distinctTaskIds = taskIds.Distinct().ToList();
        if (distinctTaskIds.Count == 0) return [];

        var filter = await taskListAccess.CreateVisibleTasksFilterAsync(userId, cancellationToken) & Builders<TaskDocument>.Filter.In(task => task.Id, distinctTaskIds);
        return [.. await database.Tasks.Find(filter).Project(task => task.Id).ToListAsync(cancellationToken)];
    }

    private static FilterDefinition<TDocument> ChangedFilter<TDocument>(long since, DateTime recentThreshold) where TDocument : DocumentBase
    {
        var filterBuilder = Builders<TDocument>.Filter;
        return filterBuilder.Gt(document => document.ChangeVersion, since) | filterBuilder.Gte(document => document.UpdatedAt, recentThreshold);
    }

    private static List<TombstoneResponse> ToTombstones<TDocument>(IEnumerable<TDocument> documents) where TDocument : DocumentBase =>
    [.. documents.Where(document => document.IsDeleted).OrderBy(document => document.ChangeVersion).Select(document => new TombstoneResponse(document.Id, document.ChangeVersion, new DateTimeOffset(DateTime.SpecifyKind(document.DeletedAt ?? document.UpdatedAt, DateTimeKind.Utc))))];
}
