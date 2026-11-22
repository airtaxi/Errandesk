using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using MongoDB.Driver;

namespace Errandesk.Server.Import;

/// <summary>
/// Copies a user's Microsoft To Do lists, tasks and checklist items into Errandesk.
/// The To Do default list merges into the user's default list, flagged email is left out, and lists and tasks imported before (including deleted ones) are skipped.
/// </summary>
public sealed class MicrosoftTodoImporter(ErrandeskDatabase database, TaskListService taskListService, UserService userService, ChangeVersionGenerator changeVersionGenerator, TaskListAccess taskListAccess, DocumentUpdater documentUpdater)
{
    private const int ProgressReportTaskInterval = 25;

    /// <summary>
    /// Imports everything <paramref name="graphClient"/> can read, updating <paramref name="counts"/> as it goes and calling <paramref name="reportProgressAsync"/> after each list and every few tasks.
    /// </summary>
    public async Task ImportAsync(Guid userId, MicrosoftGraphTodoClient graphClient, ImportCounts counts, Func<CancellationToken, Task> reportProgressAsync, CancellationToken cancellationToken)
    {
        var user = await userService.GetAsync(userId, cancellationToken);
        var today = UserTimeZones.GetToday(user.TimeZone, documentUpdater.GetUtcNow());
        var defaultList = await taskListService.EnsureDefaultListAsync(userId, cancellationToken);
        var importedLists = await database.TaskLists.Find(list => list.OwnerUserId == userId && list.ExternalSource == ImportSources.MicrosoftTodo).ToListAsync(cancellationToken);
        var importedListsByExternalId = importedLists.Where(list => list.ExternalId is not null).ToDictionary(list => list.ExternalId!, StringComparer.Ordinal);
        var importedTaskIds = (await database.Tasks.Find(task => task.OwnerUserId == userId && task.ExternalSource == ImportSources.MicrosoftTodo).Project(task => task.ExternalId).ToListAsync(cancellationToken)).OfType<string>().ToHashSet(StringComparer.Ordinal);

        var sourceLists = await graphClient.GetListsAsync(cancellationToken);
        foreach (var sourceList in sourceLists.Where(list => !list.IsFlaggedEmailsList).OrderByDescending(list => list.IsDefaultList))
        {
            TaskListDocument targetList;
            if (sourceList.IsDefaultList) targetList = defaultList;
            else if (importedListsByExternalId.TryGetValue(sourceList.Id, out var importedList))
            {
                counts.ListsSkipped++;
                if (importedList.IsDeleted) continue;
                targetList = importedList;
            }
            else
            {
                targetList = await CreateListAsync(userId, sourceList, counts, cancellationToken);
                importedListsByExternalId[sourceList.Id] = targetList;
                if (targetList.IsDeleted) continue;
            }

            await ImportTasksAsync(userId, graphClient, sourceList, targetList, user.TimeZone, today, importedTaskIds, counts, reportProgressAsync, cancellationToken);
            await reportProgressAsync(cancellationToken);
        }
    }

    private async Task ImportTasksAsync(Guid userId, MicrosoftGraphTodoClient graphClient, GraphTodoTaskList sourceList, TaskListDocument targetList, string userTimeZone, DateOnly today, HashSet<string> importedTaskIds, ImportCounts counts, Func<CancellationToken, Task> reportProgressAsync, CancellationToken cancellationToken)
    {
        var sourceTasks = await graphClient.GetTasksAsync(sourceList.Id, cancellationToken);
        var newTasks = sourceTasks.Where(task => !importedTaskIds.Contains(task.Id)).ToList();
        counts.TasksSkipped += sourceTasks.Count - newTasks.Count;
        if (newTasks.Count == 0) return;

        var firstSortOrder = await GetFirstSortOrderAsync(userId, targetList.Id, newTasks.Count, cancellationToken);
        for (var index = 0; index < newTasks.Count; index++)
        {
            var sourceTask = newTasks[index];
            var checklistItems = await graphClient.GetChecklistItemsAsync(sourceList.Id, sourceTask.Id, cancellationToken);
            var task = MicrosoftTodoTaskMapper.CreateTask(sourceTask, checklistItems, userId, userTimeZone, today, documentUpdater.GetUtcNow());
            task.Id = Guid.CreateVersion7();
            task.ListId = targetList.Id;
            task.SortOrder = firstSortOrder + index;

            if (await InsertAsync(database.Tasks, task, userId, sourceTask.CreatedDateTime, cancellationToken))
            {
                counts.TasksImported++;
                counts.StepsImported += task.Steps.Count;
            }
            else counts.TasksSkipped++;

            importedTaskIds.Add(sourceTask.Id);
            if ((index + 1) % ProgressReportTaskInterval == 0) await reportProgressAsync(cancellationToken);
        }
    }

    /// <summary>Creates the list, or returns the list another import created at the same moment for the same To Do list.</summary>
    private async Task<TaskListDocument> CreateListAsync(Guid userId, GraphTodoTaskList sourceList, ImportCounts counts, CancellationToken cancellationToken)
    {
        var lastList = await database.TaskLists.Find(list => list.OwnerUserId == userId && !list.IsDeleted).SortByDescending(list => list.SortOrder).FirstOrDefaultAsync(cancellationToken);
        var list = new TaskListDocument
        {
            Id = Guid.CreateVersion7(),
            Name = MicrosoftTodoTaskMapper.MapListName(sourceList.DisplayName),
            SortOrder = lastList is null ? 0 : Math.Floor(lastList.SortOrder) + 1,
            ExternalSource = ImportSources.MicrosoftTodo,
            ExternalId = sourceList.Id,
        };

        if (await InsertAsync(database.TaskLists, list, userId, null, cancellationToken))
        {
            counts.ListsImported++;
            return list;
        }

        counts.ListsSkipped++;
        return await database.TaskLists.Find(existing => existing.OwnerUserId == userId && existing.ExternalSource == ImportSources.MicrosoftTodo && existing.ExternalId == sourceList.Id).FirstAsync(cancellationToken);
    }

    /// <summary>
    /// The sort order of the first of <paramref name="taskCount"/> imported tasks, so they keep their To Do order: above the list's existing tasks, or from 0 in an empty list.
    /// </summary>
    private async Task<double> GetFirstSortOrderAsync(Guid userId, Guid listId, int taskCount, CancellationToken cancellationToken)
    {
        var firstTask = await database.Tasks.Find(task => task.OwnerUserId == userId && task.ListId == listId && !task.IsDeleted).SortBy(task => task.SortOrder).FirstOrDefaultAsync(cancellationToken);
        return firstTask is null ? 0 : Math.Ceiling(firstTask.SortOrder) - taskCount;
    }

    /// <summary>
    /// Stamps and inserts an imported document, keeping its original creation time when known. Returns <see langword="false"/> when the same external item was imported in the meantime.
    /// </summary>
    private async Task<bool> InsertAsync<TDocument>(IMongoCollection<TDocument> collection, TDocument document, Guid userId, DateTimeOffset? sourceCreatedAt, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        document.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
        if (sourceCreatedAt is { } createdAt) document.CreatedAt = MicrosoftTodoTaskMapper.TruncateToMilliseconds(createdAt.UtcDateTime);
        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            await taskListAccess.NotifyChangedAsync(document, document.ChangeVersion, cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.IsDuplicateKey(MongoIndexNames.UniqueExternalItem)) { return false; }
    }
}
