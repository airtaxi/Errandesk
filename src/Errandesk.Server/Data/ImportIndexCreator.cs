using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>import_jobs</c> collection and the unique external ID indexes that keep imported lists and tasks from being imported twice.
/// </summary>
public sealed class ImportIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var importJobs = database.GetCollection<ImportJobDocument>(MongoCollectionNames.ImportJobs);
        await importJobs.Indexes.CreateOneAsync(new CreateIndexModel<ImportJobDocument>(Builders<ImportJobDocument>.IndexKeys.Ascending(job => job.OwnerUserId).Ascending(job => job.Status)), cancellationToken: cancellationToken);

        var taskLists = database.GetCollection<TaskListDocument>(MongoCollectionNames.TaskLists);
        var externalListFilter = Builders<TaskListDocument>.Filter.Type(list => list.ExternalId, BsonType.String);
        await taskLists.Indexes.CreateOneAsync(new CreateIndexModel<TaskListDocument>(Builders<TaskListDocument>.IndexKeys.Ascending(list => list.OwnerUserId).Ascending(list => list.ExternalSource).Ascending(list => list.ExternalId), new CreateIndexOptions<TaskListDocument> { Name = MongoIndexNames.UniqueExternalItem, Unique = true, PartialFilterExpression = externalListFilter }), cancellationToken: cancellationToken);

        var tasks = database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks);
        var externalTaskFilter = Builders<TaskDocument>.Filter.Type(task => task.ExternalId, BsonType.String);
        await tasks.Indexes.CreateOneAsync(new CreateIndexModel<TaskDocument>(Builders<TaskDocument>.IndexKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.ExternalSource).Ascending(task => task.ExternalId), new CreateIndexOptions<TaskDocument> { Name = MongoIndexNames.UniqueExternalItem, Unique = true, PartialFilterExpression = externalTaskFilter }), cancellationToken: cancellationToken);
    }
}
