using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Decides which lists and tasks a user may see, which is the lists they own or are a member of and the tasks in those lists,
/// and tells every user who can see a changed list or task about the change.
/// </summary>
public sealed class TaskListAccess(ErrandeskDatabase database, ChangeNotifier changeNotifier)
{
    /// <summary>Selects the lists <paramref name="userId"/> owns or is a member of, deleted or not.</summary>
    public static FilterDefinition<TaskListDocument> AccessibleListsFilter(Guid userId)
    {
        var filterBuilder = Builders<TaskListDocument>.Filter;
        return filterBuilder.Eq(list => list.OwnerUserId, userId) | filterBuilder.ElemMatch(list => list.Members, member => member.UserId == userId);
    }

    /// <summary>Selects the list when it is not deleted and <paramref name="userId"/> owns it or is a member.</summary>
    public static FilterDefinition<TaskListDocument> VisibleListFilter(Guid userId, Guid listId) => Builders<TaskListDocument>.Filter.Eq(list => list.Id, listId) & Builders<TaskListDocument>.Filter.Eq(list => list.IsDeleted, false) & AccessibleListsFilter(userId);

    /// <summary>IDs of the lists <paramref name="userId"/> owns or is a member of, including deleted lists only when <paramref name="includeDeleted"/> is set.</summary>
    public async Task<List<Guid>> GetAccessibleListIdsAsync(Guid userId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var filter = AccessibleListsFilter(userId);
        if (!includeDeleted) filter &= Builders<TaskListDocument>.Filter.Eq(list => list.IsDeleted, false);
        return await database.TaskLists.Find(filter).Project(list => list.Id).ToListAsync(cancellationToken);
    }

    /// <summary>Selects the tasks <paramref name="userId"/> may see: tasks that are not deleted, in lists the user owns or is a member of.</summary>
    public async Task<FilterDefinition<TaskDocument>> CreateVisibleTasksFilterAsync(Guid userId, CancellationToken cancellationToken) => Builders<TaskDocument>.Filter.Eq(task => task.IsDeleted, false) & Builders<TaskDocument>.Filter.In(task => task.ListId, await GetAccessibleListIdsAsync(userId, false, cancellationToken));

    /// <summary>Selects the task when <paramref name="userId"/> may see it.</summary>
    public async Task<FilterDefinition<TaskDocument>> CreateVisibleTaskFilterAsync(Guid userId, Guid taskId, CancellationToken cancellationToken) => await CreateVisibleTasksFilterAsync(userId, cancellationToken) & Builders<TaskDocument>.Filter.Eq(task => task.Id, taskId);

    /// <summary>Whether the list exists, is not deleted, and <paramref name="userId"/> owns it or is a member.</summary>
    public Task<bool> CanAccessListAsync(Guid userId, Guid listId, CancellationToken cancellationToken) => database.TaskLists.Find(VisibleListFilter(userId, listId)).AnyAsync(cancellationToken);

    /// <summary>The owner and the members of the list, deleted or not; empty when the list does not exist.</summary>
    public async Task<IReadOnlyList<Guid>> GetListAudienceAsync(Guid listId, CancellationToken cancellationToken)
    {
        var list = await database.TaskLists.Find(Builders<TaskListDocument>.Filter.Eq(stored => stored.Id, listId)).FirstOrDefaultAsync(cancellationToken);
        return list is null ? [] : [.. list.GetAudience()];
    }

    /// <summary>The users who can see tasks in <paramref name="fromListId"/> but not in <paramref name="toListId"/>.</summary>
    public async Task<IReadOnlyList<Guid>> GetUsersLosingAccessAsync(Guid fromListId, Guid toListId, CancellationToken cancellationToken)
    {
        if (fromListId == toListId) return [];
        var targetAudience = await GetListAudienceAsync(toListId, cancellationToken);
        return [.. (await GetListAudienceAsync(fromListId, cancellationToken)).Except(targetAudience)];
    }

    /// <summary>
    /// The users who can see any document matching <paramref name="filter"/>: the owners, and for tasks and lists also every member of the lists involved.
    /// Read it before changing the documents, because the change can make the filter match nothing.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetAudienceAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        if (!ChangeNotifier.IsFeedDocument<TDocument>()) return [];

        HashSet<Guid> audience = [.. await collection.Distinct(document => document.OwnerUserId, filter).ToListAsync(cancellationToken)];
        if (collection is IMongoCollection<TaskDocument> taskCollection && filter is FilterDefinition<TaskDocument> taskFilter)
        {
            var listIds = await taskCollection.Distinct(task => task.ListId, taskFilter).ToListAsync(cancellationToken);
            var lists = await database.TaskLists.Find(Builders<TaskListDocument>.Filter.In(list => list.Id, listIds)).ToListAsync(cancellationToken);
            audience.UnionWith(lists.SelectMany(list => list.GetAudience()));
        }
        else if (collection is IMongoCollection<TaskListDocument> listCollection && filter is FilterDefinition<TaskListDocument> listFilter)
        {
            var lists = await listCollection.Find(listFilter).ToListAsync(cancellationToken);
            audience.UnionWith(lists.SelectMany(list => list.GetAudience()));
        }
        return [.. audience];
    }

    /// <summary>Notifies everyone who can see the changed list, task or note: the owner, and for lists and tasks also the list's members.</summary>
    public async Task NotifyChangedAsync<TDocument>(TDocument document, long changeVersion, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        IEnumerable<Guid> audience = document switch
        {
            TaskListDocument list => list.GetAudience(),
            TaskDocument task => [task.OwnerUserId, .. await GetListAudienceAsync(task.ListId, cancellationToken)],
            NoteDocument note => [note.OwnerUserId],
            _ => [],
        };
        NotifyUsers(audience, changeVersion);
    }

    /// <summary>Notifies each of <paramref name="userIds"/> once that data they can see, or could see, changed.</summary>
    public void NotifyUsers(IEnumerable<Guid> userIds, long changeVersion)
    {
        foreach (var userId in userIds.Distinct())
        {
            changeNotifier.NotifyChanged(userId, changeVersion);
        }
    }
}
