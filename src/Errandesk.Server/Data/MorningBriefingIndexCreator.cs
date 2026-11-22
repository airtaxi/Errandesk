using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes the morning briefing needs: users with briefings enabled, one briefing conversation per user, and the briefings of a user by date.
/// </summary>
public sealed class MorningBriefingIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var users = database.GetCollection<UserDocument>(MongoCollectionNames.Users);
        await users.Indexes.CreateOneAsync(new CreateIndexModel<UserDocument>(Builders<UserDocument>.IndexKeys.Ascending(user => user.MorningBriefing!.IsEnabled), new CreateIndexOptions<UserDocument> { PartialFilterExpression = Builders<UserDocument>.Filter.Eq(user => user.MorningBriefing!.IsEnabled, true) }), cancellationToken: cancellationToken);

        var conversations = database.GetCollection<ConversationDocument>(MongoCollectionNames.Conversations);
        var briefingConversationFilter = Builders<ConversationDocument>.Filter.Eq(conversation => conversation.IsBriefing, true) & Builders<ConversationDocument>.Filter.Eq(conversation => conversation.IsDeleted, false);
        await conversations.Indexes.CreateOneAsync(new CreateIndexModel<ConversationDocument>(Builders<ConversationDocument>.IndexKeys.Ascending(conversation => conversation.OwnerUserId), new CreateIndexOptions<ConversationDocument> { Name = MongoIndexNames.UniqueBriefingConversation, Unique = true, PartialFilterExpression = briefingConversationFilter }), cancellationToken: cancellationToken);

        var messages = database.GetCollection<ConversationMessageDocument>(MongoCollectionNames.ConversationMessages);
        var messageKeys = Builders<ConversationMessageDocument>.IndexKeys.Ascending(message => message.OwnerUserId).Descending(message => message.CreatedAt);
        await messages.Indexes.CreateOneAsync(new CreateIndexModel<ConversationMessageDocument>(messageKeys, new CreateIndexOptions<ConversationMessageDocument> { PartialFilterExpression = Builders<ConversationMessageDocument>.Filter.Type(message => message.BriefingDate, BsonType.String) }), cancellationToken: cancellationToken);
    }
}
