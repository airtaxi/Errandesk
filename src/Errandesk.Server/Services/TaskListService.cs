using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Creates, reads, changes, shares and deletes task lists, including the default list every user has.
/// A user sees the lists they own and the lists shared with them; only the owner renames, deletes and shares a list, and members can leave it.
/// </summary>
public sealed class TaskListService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, TaskListAccess taskListAccess, DocumentUpdater documentUpdater)
{
    public const int MaximumMemberCount = 50;

    private const string ResourceName = "list";
    private const string MemberResourceName = "member";
    private const string UserResourceName = "user";

    /// <summary>The lists the user owns or is a member of, in ascending sort order, creating the user's default list when it is missing.</summary>
    public async Task<IReadOnlyList<TaskListResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await EnsureDefaultListAsync(userId, cancellationToken);
        var filter = TaskListAccess.AccessibleListsFilter(userId) & Builders<TaskListDocument>.Filter.Eq(list => list.IsDeleted, false);
        var lists = await database.TaskLists.Find(filter).SortBy(list => list.SortOrder).ThenBy(list => list.CreatedAt).ToListAsync(cancellationToken);
        return await ToResponsesAsync(lists, cancellationToken);
    }

    public async Task<TaskListResponse> GetAsync(Guid userId, Guid listId, CancellationToken cancellationToken) => await ToResponseAsync(await GetVisibleAsync(userId, listId, cancellationToken), cancellationToken);

    /// <summary>The list when it exists, is not deleted and the user owns it or is a member; otherwise throws <see cref="ResourceNotFoundException"/>.</summary>
    public async Task<TaskListDocument> GetVisibleAsync(Guid userId, Guid listId, CancellationToken cancellationToken) => await database.TaskLists.Find(TaskListAccess.VisibleListFilter(userId, listId)).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    public async Task<CreateResult<TaskListResponse>> CreateAsync(Guid userId, CreateTaskListRequest request, CancellationToken cancellationToken)
    {
        var name = InputValidation.RequireText(request.Name, "list name", InputValidation.MaximumListNameLength);
        var listId = InputValidation.ClientId(request.Id);
        var sortOrder = request.SortOrder is { } requestedSortOrder ? InputValidation.SortOrder(requestedSortOrder) : await GetNextSortOrderAsync(userId, cancellationToken);

        var list = new TaskListDocument { Id = listId, Name = name, SortOrder = sortOrder };
        var result = await documentUpdater.InsertIdempotentAsync(database.TaskLists, list, userId, ResourceName, cancellationToken);
        return new CreateResult<TaskListResponse>(await ToResponseAsync(result.Resource, cancellationToken), result.IsCreated);
    }

    /// <summary>Renames or moves a list. Only the owner may change it.</summary>
    public async Task<TaskListResponse> UpdateAsync(Guid userId, Guid listId, UpdateTaskListRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.IsSpecified ? InputValidation.RequireText(request.Name.Value, "list name", InputValidation.MaximumListNameLength) : null;
        var sortOrder = request.SortOrder.IsSpecified ? InputValidation.SortOrder(request.SortOrder.Value) : (double?)null;

        try
        {
            var list = await documentUpdater.UpdateAsync(database.TaskLists, TaskListAccess.VisibleListFilter(userId, listId), ResourceName, request.ExpectedChangeVersion, current => current, current =>
            {
                RequireOwner(current, userId, "Only the owner of the list can change it.");
                if (name is not null && current.IsDefault) throw new RequestValidationException("The default list cannot be renamed.");
                if (name is not null) current.Name = name;
                if (sortOrder is { } newSortOrder) current.SortOrder = newSortOrder;
            }, cancellationToken);
            return await ToResponseAsync(list, cancellationToken);
        }
        catch (ChangeVersionConflictException exception) when (exception.CurrentResource is TaskListDocument currentList) { throw new ChangeVersionConflictException(await ToResponseAsync(currentList, cancellationToken)); }
    }

    /// <summary>Soft-deletes a list together with its tasks. Only the owner may delete a list, and the default list cannot be deleted.</summary>
    public async Task DeleteAsync(Guid userId, Guid listId, CancellationToken cancellationToken)
    {
        var list = await GetVisibleAsync(userId, listId, cancellationToken);
        RequireOwner(list, userId, "Only the owner of the list can delete it. Members can leave it instead.");
        if (list.IsDefault) throw new RequestValidationException("The default list cannot be deleted.");

        var tasksInListFilter = Builders<TaskDocument>.Filter.Where(task => task.ListId == listId && !task.IsDeleted);
        await documentUpdater.SoftDeleteManyAsync(database.Tasks, tasksInListFilter, cancellationToken);
        await documentUpdater.SoftDeleteAsync(database.TaskLists, TaskListAccess.VisibleListFilter(userId, listId), ResourceName, cancellationToken);
        await documentUpdater.SoftDeleteManyAsync(database.Tasks, tasksInListFilter, cancellationToken);
    }

    /// <summary>
    /// Shares the list with the user whose user name equals <paramref name="request"/>'s name, ignoring case. Only the owner may share, and the default list cannot be shared.
    /// An unknown user name gives the same not-found answer as any other name that matches nobody, and names are never searched or completed.
    /// Adding an existing member returns the list unchanged.
    /// </summary>
    public async Task<TaskListResponse> AddMemberAsync(Guid userId, Guid listId, AddTaskListMemberRequest request, CancellationToken cancellationToken)
    {
        var list = await GetVisibleAsync(userId, listId, cancellationToken);
        RequireOwner(list, userId, "Only the owner of the list can share it.");
        if (list.IsDefault) throw new RequestValidationException("The default list cannot be shared.");

        var userName = request.UserName?.Trim() ?? "";
        if (userName.Length == 0) throw new RequestValidationException("The user name must not be empty.");
        var normalizedUserName = InputValidation.NormalizeUserName(userName);
        var member = await database.Users.Find(user => user.NormalizedUserName == normalizedUserName && !user.IsDeleted).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(UserResourceName);
        if (member.Id == userId) throw new RequestValidationException("You already own this list.");
        if (list.Members.Exists(existing => existing.UserId == member.Id)) return await ToResponseAsync(list, cancellationToken);

        var grantChangeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
        var addedAt = documentUpdater.GetUtcNow();
        var updatedList = await documentUpdater.UpdateAsync(database.TaskLists, TaskListAccess.VisibleListFilter(userId, listId), ResourceName, null, current => current, current =>
        {
            RequireOwner(current, userId, "Only the owner of the list can share it.");
            if (current.Members.Exists(existing => existing.UserId == member.Id)) return;
            if (current.Members.Count >= MaximumMemberCount) throw new RequestValidationException($"A list can be shared with at most {MaximumMemberCount} users.");
            current.Members.Add(new TaskListMemberDocument { UserId = member.Id, AddedAt = addedAt, ChangeVersion = grantChangeVersion });
            current.FormerMemberUserIds.Remove(member.Id);
        }, cancellationToken);
        return await ToResponseAsync(updatedList, cancellationToken);
    }

    /// <summary>
    /// Removes a member from the list. The owner may remove anyone; a member may only remove themselves, which leaves the list.
    /// The removed user's change feed then tells their devices to drop the list and its tasks.
    /// </summary>
    public async Task RemoveMemberAsync(Guid userId, Guid listId, Guid memberUserId, CancellationToken cancellationToken)
    {
        var list = await GetVisibleAsync(userId, listId, cancellationToken);
        if (list.OwnerUserId != userId && memberUserId != userId) throw new OperationNotAllowedException("Only the owner of the list can remove other members.");
        if (memberUserId == list.OwnerUserId) throw new RequestValidationException("The owner cannot leave their own list.");

        var updatedList = await documentUpdater.UpdateAsync(database.TaskLists, TaskListAccess.VisibleListFilter(userId, listId), ResourceName, null, current => current, current =>
        {
            if (current.Members.RemoveAll(member => member.UserId == memberUserId) == 0) throw new ResourceNotFoundException(MemberResourceName);
            if (!current.FormerMemberUserIds.Contains(memberUserId)) current.FormerMemberUserIds.Add(memberUserId);
        }, cancellationToken);
        taskListAccess.NotifyUsers([memberUserId], updatedList.ChangeVersion);
    }

    /// <summary>The user's default list, created when it does not exist yet.</summary>
    public async Task<TaskListDocument> EnsureDefaultListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var defaultList = await FindDefaultListAsync(userId, cancellationToken);
        if (defaultList is not null) return defaultList;

        defaultList = new TaskListDocument { Id = Guid.CreateVersion7(), Name = TaskListDocument.DefaultListName, IsDefault = true };
        defaultList.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
        try
        {
            await database.TaskLists.InsertOneAsync(defaultList, cancellationToken: cancellationToken);
            taskListAccess.NotifyUsers([userId], defaultList.ChangeVersion);
            return defaultList;
        }
        catch (MongoWriteException exception) when (exception.IsDuplicateKey(MongoIndexNames.UniqueDefaultList)) { return await FindDefaultListAsync(userId, cancellationToken) ?? throw new ResourceNotFoundException(ResourceName); }
    }

    /// <summary>Maps lists to responses with the names of their owners and members, loading every user involved with one query.</summary>
    public async Task<IReadOnlyList<TaskListResponse>> ToResponsesAsync(IReadOnlyList<TaskListDocument> lists, CancellationToken cancellationToken)
    {
        var userIds = lists.SelectMany(list => list.GetAudience()).Distinct().ToList();
        List<UserDocument> users = userIds.Count == 0 ? [] : await database.Users.Find(Builders<UserDocument>.Filter.In(user => user.Id, userIds)).ToListAsync(cancellationToken);
        var usersById = users.ToDictionary(user => user.Id);
        return [.. lists.Select(list => list.ToResponse(usersById))];
    }

    private async Task<TaskListResponse> ToResponseAsync(TaskListDocument list, CancellationToken cancellationToken) => (await ToResponsesAsync([list], cancellationToken))[0];

    private static void RequireOwner(TaskListDocument list, Guid userId, string detail)
    {
        if (list.OwnerUserId != userId)
        {
            throw new OperationNotAllowedException(detail);
        }
    }

    private Task<TaskListDocument?> FindDefaultListAsync(Guid userId, CancellationToken cancellationToken) => database.TaskLists.Find(list => list.OwnerUserId == userId && list.IsDefault && !list.IsDeleted).FirstOrDefaultAsync(cancellationToken)!;

    private async Task<double> GetNextSortOrderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lastList = await database.TaskLists.Find(list => list.OwnerUserId == userId && !list.IsDeleted).SortByDescending(list => list.SortOrder).FirstOrDefaultAsync(cancellationToken);
        return lastList is null ? 0 : Math.Floor(lastList.SortOrder) + 1;
    }
}
