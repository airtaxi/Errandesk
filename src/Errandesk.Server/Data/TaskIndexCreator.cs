using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>task_lists</c> and <c>tasks</c> collections.
/// </summary>
public sealed class TaskIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var taskLists = database.GetCollection<TaskListDocument>(MongoCollectionNames.TaskLists);
        var taskListKeys = Builders<TaskListDocument>.IndexKeys;
        var defaultListFilter = Builders<TaskListDocument>.Filter.Eq(list => list.IsDefault, true) & Builders<TaskListDocument>.Filter.Eq(list => list.IsDeleted, false);
        await taskLists.Indexes.CreateManyAsync([new CreateIndexModel<TaskListDocument>(taskListKeys.Ascending(list => list.OwnerUserId).Ascending(list => list.IsDeleted)), new CreateIndexModel<TaskListDocument>(taskListKeys.Ascending(list => list.OwnerUserId).Ascending(list => list.ChangeVersion)), new CreateIndexModel<TaskListDocument>(taskListKeys.Ascending(list => list.OwnerUserId), new CreateIndexOptions<TaskListDocument> { Name = MongoIndexNames.UniqueDefaultList, Unique = true, PartialFilterExpression = defaultListFilter }),], cancellationToken);

        var tasks = database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks);
        var taskKeys = Builders<TaskDocument>.IndexKeys;
        await tasks.Indexes.CreateManyAsync([new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.ListId)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.MyDayDate)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.IsImportant)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.DueDate)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.OwnerUserId).Ascending(task => task.ChangeVersion)),], cancellationToken);
    }
}
