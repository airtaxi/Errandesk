using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Applies changes to visible documents with optimistic concurrency, stamping the update time and a new global change version.
/// </summary>
public sealed class DocumentUpdater(ChangeVersionGenerator changeVersionGenerator, TaskListAccess taskListAccess, TimeProvider timeProvider)
{
    private const int MaximumAttempts = 5;
    private const string IdIndexName = "_id_";

    /// <summary>
    /// Loads the document matching <paramref name="visibleFilter"/>, applies <paramref name="applyChanges"/> and replaces it only if nobody changed it in between, reloading and reapplying otherwise.
    /// </summary>
    /// <param name="collection">Collection that stores the document.</param>
    /// <param name="visibleFilter">Selects the document only when the caller may see it.</param>
    /// <param name="resourceName">Name used in the not-found message.</param>
    /// <param name="expectedChangeVersion">When set, a different stored change version fails with <see cref="ChangeVersionConflictException"/>.</param>
    /// <param name="createConflictResponse">Builds the response body of a change version conflict from the current document.</param>
    /// <param name="applyChanges">Changes the loaded document in memory. May throw domain exceptions.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<TDocument> UpdateAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> visibleFilter, string resourceName, long? expectedChangeVersion, Func<TDocument, object> createConflictResponse, Action<TDocument> applyChanges, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        for (var attempt = 1; ; attempt++)
        {
            var document = await collection.Find(visibleFilter).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(resourceName);
            if (expectedChangeVersion is { } expectedVersion && document.ChangeVersion != expectedVersion) throw new ChangeVersionConflictException(createConflictResponse(document));

            var loadedChangeVersion = document.ChangeVersion;
            applyChanges(document);
            document.StampUpdated(GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));

            var unchangedFilter = visibleFilter & Builders<TDocument>.Filter.Eq(stored => stored.ChangeVersion, loadedChangeVersion);
            var result = await collection.ReplaceOneAsync(unchangedFilter, document, cancellationToken: cancellationToken);
            if (result.MatchedCount == 1)
            {
                await taskListAccess.NotifyChangedAsync(document, document.ChangeVersion, cancellationToken);
                return document;
            }

            if (attempt == MaximumAttempts) throw new ResourceConflictException($"The {resourceName} is being changed by another request. Try again.");
        }
    }

    /// <summary>Marks the document matching <paramref name="visibleFilter"/> as deleted, keeping it as a tombstone.</summary>
    public async Task SoftDeleteAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> visibleFilter, string resourceName, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var (update, changeVersion) = await CreateSoftDeleteUpdateAsync<TDocument>(cancellationToken);
        var deletedDocument = await collection.FindOneAndUpdateAsync(visibleFilter, update, cancellationToken: cancellationToken) ?? throw new ResourceNotFoundException(resourceName);
        await taskListAccess.NotifyChangedAsync(deletedDocument, changeVersion, cancellationToken);
    }

    /// <summary>Marks every document matching <paramref name="filter"/> as deleted with one shared change version.</summary>
    public async Task SoftDeleteManyAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        if (!await collection.Find(filter).AnyAsync(cancellationToken)) return;

        var audience = await taskListAccess.GetAudienceAsync(collection, filter, cancellationToken);
        var (update, changeVersion) = await CreateSoftDeleteUpdateAsync<TDocument>(cancellationToken);
        await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        taskListAccess.NotifyUsers(audience, changeVersion);
    }

    /// <summary>
    /// Inserts a new document owned by <paramref name="ownerUserId"/>. When a document with the same ID already exists for the same owner, returns it instead.
    /// </summary>
    public async Task<CreateResult<TDocument>> InsertIdempotentAsync<TDocument>(IMongoCollection<TDocument> collection, TDocument document, Guid ownerUserId, string resourceName, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var existingDocument = await FindExistingAsync(collection, document.Id, ownerUserId, resourceName, cancellationToken);
        if (existingDocument is not null) return new CreateResult<TDocument>(existingDocument, false);

        document.StampCreated(ownerUserId, GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            await taskListAccess.NotifyChangedAsync(document, document.ChangeVersion, cancellationToken);
            return new CreateResult<TDocument>(document, true);
        }
        catch (MongoWriteException exception) when (exception.IsDuplicateKey(IdIndexName))
        {
            existingDocument = await FindExistingAsync(collection, document.Id, ownerUserId, resourceName, cancellationToken);
            return existingDocument is not null ? new CreateResult<TDocument>(existingDocument, false) : throw new ResourceConflictException($"Another {resourceName} already uses this ID.");
        }
    }

    /// <summary>The current UTC time truncated to milliseconds, the precision MongoDB stores, so returned and stored values match.</summary>
    public DateTime GetUtcNow()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return utcNow.AddTicks(-(utcNow.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static async Task<TDocument?> FindExistingAsync<TDocument>(IMongoCollection<TDocument> collection, Guid identifier, Guid ownerUserId, string resourceName, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var existingDocument = await collection.Find(Builders<TDocument>.Filter.Eq(document => document.Id, identifier)).FirstOrDefaultAsync(cancellationToken);
        if (existingDocument is null) return null;
        if (existingDocument.OwnerUserId != ownerUserId) throw new ResourceConflictException($"Another {resourceName} already uses this ID.");
        if (existingDocument.IsDeleted) throw new ResourceConflictException($"The {resourceName} with this ID was deleted.");
        return existingDocument;
    }

    private async Task<(UpdateDefinition<TDocument> Update, long ChangeVersion)> CreateSoftDeleteUpdateAsync<TDocument>(CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var utcNow = GetUtcNow();
        var changeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
        return (Builders<TDocument>.Update.Set(document => document.IsDeleted, true).Set(document => document.DeletedAt, utcNow).Set(document => document.UpdatedAt, utcNow).Set(document => document.ChangeVersion, changeVersion), changeVersion);
    }
}
