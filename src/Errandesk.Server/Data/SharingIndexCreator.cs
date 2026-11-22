using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes that find shared lists by member, the tasks of lists, and the access revocations and personal My Day dates of tasks.
/// </summary>
public sealed class SharingIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var taskLists = database.GetCollection<TaskListDocument>(MongoCollectionNames.TaskLists);
        var taskListKeys = Builders<TaskListDocument>.IndexKeys;
        await taskLists.Indexes.CreateManyAsync([new CreateIndexModel<TaskListDocument>(taskListKeys.Ascending("members.userId")), new CreateIndexModel<TaskListDocument>(taskListKeys.Ascending(list => list.FormerMemberUserIds)),], cancellationToken);

        var tasks = database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks);
        var taskKeys = Builders<TaskDocument>.IndexKeys;
        await tasks.Indexes.CreateManyAsync([new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.ListId).Ascending(task => task.ChangeVersion)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending(task => task.RevokedUserIds)), new CreateIndexModel<TaskDocument>(taskKeys.Ascending("memberMyDayDates.userId").Ascending("memberMyDayDates.date")),], cancellationToken);
    }
}
