using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>conversations</c>, <c>conversation_messages</c>, <c>conversation_attachments</c> and <c>agent_actions</c> collections.
/// Unsent attachments expire at their <see cref="ConversationAttachmentDocument.UnsentExpiresAt"/>.
/// </summary>
public sealed class AssistantIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var attachments = database.GetCollection<ConversationAttachmentDocument>(MongoCollectionNames.ConversationAttachments);
        var attachmentKeys = Builders<ConversationAttachmentDocument>.IndexKeys;
        await attachments.Indexes.CreateManyAsync([new CreateIndexModel<ConversationAttachmentDocument>(attachmentKeys.Ascending(attachment => attachment.ConversationId)), new CreateIndexModel<ConversationAttachmentDocument>(attachmentKeys.Ascending(attachment => attachment.UnsentExpiresAt), new CreateIndexOptions { Name = MongoIndexNames.UnsentAttachmentExpiry, ExpireAfter = TimeSpan.Zero })], cancellationToken);

        var conversations = database.GetCollection<ConversationDocument>(MongoCollectionNames.Conversations);
        var conversationKeys = Builders<ConversationDocument>.IndexKeys;
        await conversations.Indexes.CreateOneAsync(new CreateIndexModel<ConversationDocument>(conversationKeys.Ascending(conversation => conversation.OwnerUserId).Ascending(conversation => conversation.IsDeleted).Descending(conversation => conversation.UpdatedAt)), cancellationToken: cancellationToken);

        var messages = database.GetCollection<ConversationMessageDocument>(MongoCollectionNames.ConversationMessages);
        var messageKeys = Builders<ConversationMessageDocument>.IndexKeys;
        await messages.Indexes.CreateOneAsync(new CreateIndexModel<ConversationMessageDocument>(messageKeys.Ascending(message => message.ConversationId).Ascending(message => message.Sequence), new CreateIndexOptions { Name = MongoIndexNames.UniqueConversationMessageSequence, Unique = true }), cancellationToken: cancellationToken);

        var agentActions = database.GetCollection<AgentActionDocument>(MongoCollectionNames.AgentActions);
        var agentActionKeys = Builders<AgentActionDocument>.IndexKeys;
        await agentActions.Indexes.CreateManyAsync([new CreateIndexModel<AgentActionDocument>(agentActionKeys.Ascending(action => action.OwnerUserId).Descending(action => action.CreatedAt)), new CreateIndexModel<AgentActionDocument>(agentActionKeys.Ascending(action => action.ConversationId))], cancellationToken);
    }
}
