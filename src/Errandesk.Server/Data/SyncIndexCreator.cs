using Errandesk.Server.Services;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the TTL index of the <c>idempotency_keys</c> collection and the indexes the tombstone purge uses to find old tombstones.
/// </summary>
public sealed class SyncIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var idempotencyKeys = database.GetCollection<IdempotencyKeyDocument>(MongoCollectionNames.IdempotencyKeys);
        await idempotencyKeys.Indexes.CreateOneAsync(new CreateIndexModel<IdempotencyKeyDocument>(Builders<IdempotencyKeyDocument>.IndexKeys.Ascending(document => document.CreatedAt), new CreateIndexOptions { Name = MongoIndexNames.IdempotencyKeyExpiry, ExpireAfter = IdempotencyKeyStore.Retention }), cancellationToken: cancellationToken);

        await CreateTombstoneIndexAsync(database.GetCollection<TaskListDocument>(MongoCollectionNames.TaskLists), cancellationToken);
        await CreateTombstoneIndexAsync(database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks), cancellationToken);
        await CreateTombstoneIndexAsync(database.GetCollection<NoteDocument>(MongoCollectionNames.Notes), cancellationToken);
    }

    private static Task<string> CreateTombstoneIndexAsync<TDocument>(IMongoCollection<TDocument> collection, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var keys = Builders<TDocument>.IndexKeys.Ascending(document => document.DeletedAt).Ascending(document => document.UpdatedAt);
        var options = new CreateIndexOptions<TDocument> { Name = MongoIndexNames.Tombstones, PartialFilterExpression = Builders<TDocument>.Filter.Eq(document => document.IsDeleted, true) };
        return collection.Indexes.CreateOneAsync(new CreateIndexModel<TDocument>(keys, options), cancellationToken: cancellationToken);
    }
}
