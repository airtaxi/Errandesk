using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Typed access to the collections of the configured database.
/// </summary>
public sealed class ErrandeskDatabase(IMongoDatabase database)
{
    public IMongoCollection<UserDocument> Users { get; } = database.GetCollection<UserDocument>(MongoCollectionNames.Users);

    public IMongoCollection<InviteDocument> Invites { get; } = database.GetCollection<InviteDocument>(MongoCollectionNames.Invites);

    public IMongoCollection<RefreshTokenDocument> RefreshTokens { get; } = database.GetCollection<RefreshTokenDocument>(MongoCollectionNames.RefreshTokens);

    public IMongoCollection<TaskListDocument> TaskLists { get; } = database.GetCollection<TaskListDocument>(MongoCollectionNames.TaskLists);

    public IMongoCollection<TaskDocument> Tasks { get; } = database.GetCollection<TaskDocument>(MongoCollectionNames.Tasks);

    public IMongoCollection<NoteDocument> Notes { get; } = database.GetCollection<NoteDocument>(MongoCollectionNames.Notes);

    public IMongoCollection<ChangeCounterDocument> ChangeCounters { get; } = database.GetCollection<ChangeCounterDocument>(MongoCollectionNames.ChangeCounters);

    public IMongoCollection<ConversationDocument> Conversations { get; } = database.GetCollection<ConversationDocument>(MongoCollectionNames.Conversations);

    public IMongoCollection<ConversationMessageDocument> ConversationMessages { get; } = database.GetCollection<ConversationMessageDocument>(MongoCollectionNames.ConversationMessages);

    public IMongoCollection<ConversationAttachmentDocument> ConversationAttachments { get; } = database.GetCollection<ConversationAttachmentDocument>(MongoCollectionNames.ConversationAttachments);

    public IMongoCollection<AgentActionDocument> AgentActions { get; } = database.GetCollection<AgentActionDocument>(MongoCollectionNames.AgentActions);

    public IMongoCollection<DeviceDocument> Devices { get; } = database.GetCollection<DeviceDocument>(MongoCollectionNames.Devices);

    public IMongoCollection<IdempotencyKeyDocument> IdempotencyKeys { get; } = database.GetCollection<IdempotencyKeyDocument>(MongoCollectionNames.IdempotencyKeys);

    public IMongoCollection<ImportJobDocument> ImportJobs { get; } = database.GetCollection<ImportJobDocument>(MongoCollectionNames.ImportJobs);

    public IMongoCollection<ApiTokenDocument> ApiTokens { get; } = database.GetCollection<ApiTokenDocument>(MongoCollectionNames.ApiTokens);
}
