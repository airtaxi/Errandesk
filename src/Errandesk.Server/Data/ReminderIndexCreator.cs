using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the index the reminder dispatcher uses to find due reminders, and the indexes of the <c>devices</c> collection.
/// </summary>
public sealed class ReminderIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var tasks = database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks);
        var reminderFilter = Builders<TaskDocument>.Filter.Type(task => task.ReminderAt, BsonType.DateTime);
        await tasks.Indexes.CreateOneAsync(new CreateIndexModel<TaskDocument>(Builders<TaskDocument>.IndexKeys.Ascending(task => task.ReminderAt), new CreateIndexOptions<TaskDocument> { Name = MongoIndexNames.DueReminders, PartialFilterExpression = reminderFilter }), cancellationToken: cancellationToken);

        var devices = database.GetCollection<DeviceDocument>(MongoCollectionNames.Devices);
        var deviceKeys = Builders<DeviceDocument>.IndexKeys;
        await devices.Indexes.CreateManyAsync([new CreateIndexModel<DeviceDocument>(deviceKeys.Ascending(device => device.OwnerUserId)), new CreateIndexModel<DeviceDocument>(deviceKeys.Ascending(device => device.PushToken), new CreateIndexOptions<DeviceDocument> { Sparse = true }),], cancellationToken);
    }
}
