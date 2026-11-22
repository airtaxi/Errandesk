using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Records the changes assistant tools make, with snapshots of the affected documents, and undoes them.
/// </summary>
public sealed class AgentActionService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, TaskListAccess taskListAccess, DocumentUpdater documentUpdater)
{
    private const string ResourceName = "agent action";

    private static readonly string s_changeVersionElementName = JsonNamingPolicy.CamelCase.ConvertName(nameof(DocumentBase.ChangeVersion));

    /// <summary>
    /// The stored form of a list or task the user owns or shares, or of a note of the user, deleted or not, or <see langword="null"/> when it does not exist or the user cannot see it.
    /// </summary>
    public async Task<BsonDocument?> CaptureAsync(string entityType, Guid userId, Guid entityId, CancellationToken cancellationToken) => entityType switch
    {
        EntityTypes.List => await CaptureAsync(database.TaskLists, TaskListAccess.AccessibleListsFilter(userId) & Builders<TaskListDocument>.Filter.Eq(list => list.Id, entityId), cancellationToken),
        EntityTypes.Task => await CaptureAsync(database.Tasks, Builders<TaskDocument>.Filter.Eq(task => task.Id, entityId) & Builders<TaskDocument>.Filter.In(task => task.ListId, await taskListAccess.GetAccessibleListIdsAsync(userId, true, cancellationToken)), cancellationToken),
        EntityTypes.Note => await CaptureAsync(database.Notes, Builders<NoteDocument>.Filter.Where(note => note.Id == entityId && note.OwnerUserId == userId), cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type."),
    };

    /// <summary>The stored form of every task that is in a list the user can see and not deleted, by task ID.</summary>
    public async Task<Dictionary<Guid, BsonDocument>> CaptureTasksInListAsync(Guid userId, Guid listId, CancellationToken cancellationToken)
    {
        if (!await taskListAccess.CanAccessListAsync(userId, listId, cancellationToken)) return [];
        var tasks = await database.Tasks.Find(task => task.ListId == listId && !task.IsDeleted).ToListAsync(cancellationToken);
        return tasks.ToDictionary(task => task.Id, task => task.ToBsonDocument());
    }

    /// <summary>
    /// Stores an agent action for the given entities, the primary entity first. Returns <see langword="null"/> without storing anything when no entity changed.
    /// </summary>
    public async Task<Guid?> RecordAsync(Guid userId, Guid? conversationId, string toolName, string argumentsJson, IReadOnlyList<AgentActionEntityDocument> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0 || entities.All(entity => entity.Before is not null && ReadChangeVersion(entity.Before) == ReadChangeVersion(entity.After))) return null;

        var action = new AgentActionDocument
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            ToolName = toolName,
            ArgumentsJson = argumentsJson,
            EntityType = entities[0].EntityType,
            EntityId = entities[0].EntityId,
            Entities = [.. entities],
        };
        action.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
        await database.AgentActions.InsertOneAsync(action, cancellationToken: cancellationToken);
        return action.Id;
    }

    public async Task<AgentActionResponse> GetAsync(Guid userId, Guid actionId, CancellationToken cancellationToken) => (await GetVisibleAsync(userId, actionId, cancellationToken)).ToResponse();

    /// <summary>IDs among <paramref name="actionIds"/> of the user's actions that were undone.</summary>
    public async Task<HashSet<Guid>> GetUndoneIdsAsync(Guid userId, IReadOnlyCollection<Guid> actionIds, CancellationToken cancellationToken)
    {
        if (actionIds.Count == 0) return [];
        var filter = Builders<AgentActionDocument>.Filter.Where(action => action.OwnerUserId == userId && action.UndoneAt != null) & Builders<AgentActionDocument>.Filter.In(action => action.Id, actionIds);
        return [.. await database.AgentActions.Find(filter).Project(action => action.Id).ToListAsync(cancellationToken)];
    }

    /// <summary>
    /// Restores every affected document to its state before the action: created items are deleted, deleted items are restored and changed items get their previous values.
    /// Fails with <see cref="ResourceConflictException"/> when the action was already undone or any affected document changed since.
    /// </summary>
    public async Task<AgentActionResponse> UndoAsync(Guid userId, Guid actionId, CancellationToken cancellationToken)
    {
        var action = await GetVisibleAsync(userId, actionId, cancellationToken);
        if (action.UndoneAt is not null) throw new ResourceConflictException("The action was already undone.");
        foreach (var entity in action.Entities) await EnsureRestorableAsync(userId, action, entity, cancellationToken);

        var utcNow = documentUpdater.GetUtcNow();
        var claimFilter = Builders<AgentActionDocument>.Filter.Where(stored => stored.Id == actionId && stored.UndoneAt == null);
        var claimedAction = await database.AgentActions.FindOneAndUpdateAsync(claimFilter, Builders<AgentActionDocument>.Update.Set(stored => stored.UndoneAt, utcNow).Set(stored => stored.UpdatedAt, utcNow).Set(stored => stored.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken)), new FindOneAndUpdateOptions<AgentActionDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken) ?? throw new ResourceConflictException("The action was already undone.");

        try
        {
            foreach (var entity in action.Entities)
            {
                await RestoreAsync(entity, utcNow, cancellationToken);
            }
        }
        catch (ResourceConflictException)
        {
            await database.AgentActions.UpdateOneAsync(stored => stored.Id == actionId, Builders<AgentActionDocument>.Update.Set(stored => stored.UndoneAt, null), cancellationToken: CancellationToken.None);
            throw;
        }
        return claimedAction.ToResponse();
    }

    private async Task<AgentActionDocument> GetVisibleAsync(Guid userId, Guid actionId, CancellationToken cancellationToken) => await database.AgentActions.Find(action => action.Id == actionId && action.OwnerUserId == userId && !action.IsDeleted).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    private async Task EnsureRestorableAsync(Guid userId, AgentActionDocument action, AgentActionEntityDocument entity, CancellationToken cancellationToken)
    {
        var current = await CaptureAsync(entity.EntityType, userId, entity.EntityId, cancellationToken);
        if (current is null || ReadChangeVersion(current) != ReadChangeVersion(entity.After)) throw new ResourceConflictException($"The {entity.EntityType} was changed after the assistant changed it.");
        if (entity.EntityType != EntityTypes.Task || entity.Before is null) return;

        var listId = BsonSerializer.Deserialize<TaskDocument>(entity.Before).ListId;
        var isListRestoredToo = action.Entities.Any(other => other.EntityType == EntityTypes.List && other.EntityId == listId);
        if (!isListRestoredToo && !await taskListAccess.CanAccessListAsync(userId, listId, cancellationToken)) throw new ResourceConflictException("The list of the task was deleted.");
    }

    private Task RestoreAsync(AgentActionEntityDocument entity, DateTime utcNow, CancellationToken cancellationToken) => entity.EntityType switch
    {
        EntityTypes.List => RestoreAsync(database.TaskLists, entity, utcNow, cancellationToken),
        EntityTypes.Task => RestoreAsync(database.Tasks, entity, utcNow, cancellationToken),
        EntityTypes.Note => RestoreAsync(database.Notes, entity, utcNow, cancellationToken),
        _ => throw new InvalidOperationException($"Unknown entity type {entity.EntityType}."),
    };

    /// <summary>
    /// Replaces the document with its state before the action. A task that returns to another list records the users who lose sight of it, so their change feed drops it.
    /// </summary>
    private async Task RestoreAsync<TDocument>(IMongoCollection<TDocument> collection, AgentActionEntityDocument entity, DateTime utcNow, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var expectedChangeVersion = ReadChangeVersion(entity.After);
        var restored = BsonSerializer.Deserialize<TDocument>(entity.Before ?? entity.After);
        if (entity.Before is null)
        {
            restored.IsDeleted = true;
            restored.DeletedAt = utcNow;
        }

        IReadOnlyList<Guid> usersLosingAccess = [];
        if (restored is TaskDocument restoredTask)
        {
            usersLosingAccess = await taskListAccess.GetUsersLosingAccessAsync(BsonSerializer.Deserialize<TaskDocument>(entity.After).ListId, restoredTask.ListId, cancellationToken);
            var restoredAudience = await taskListAccess.GetListAudienceAsync(restoredTask.ListId, cancellationToken);
            restoredTask.RevokedUserIds = [.. restoredTask.RevokedUserIds.Union(usersLosingAccess).Except(restoredAudience)];
        }

        restored.StampUpdated(utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
        var filter = Builders<TDocument>.Filter.Where(document => document.Id == entity.EntityId && document.ChangeVersion == expectedChangeVersion);
        var result = await collection.ReplaceOneAsync(filter, restored, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0) throw new ResourceConflictException($"The {entity.EntityType} was changed after the assistant changed it.");
        await taskListAccess.NotifyChangedAsync(restored, restored.ChangeVersion, cancellationToken);
        taskListAccess.NotifyUsers(usersLosingAccess, restored.ChangeVersion);
    }

    private static async Task<BsonDocument?> CaptureAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var document = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document?.ToBsonDocument();
    }

    private static long ReadChangeVersion(BsonDocument snapshot) => snapshot[s_changeVersionElementName].ToInt64();
}
