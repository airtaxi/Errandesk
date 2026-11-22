using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Keeps each user's briefing conversation and the morning briefings stored in it.
/// </summary>
public sealed class MorningBriefingService(ErrandeskDatabase database, ConversationService conversationService, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater)
{
    /// <summary>Title of the briefing conversation. Apps show their own localized title for it.</summary>
    public const string ConversationTitle = "Morning briefing";

    /// <summary>The most recent briefing of the user, or <see langword="null"/> when none is stored.</summary>
    public async Task<MorningBriefingResponse?> GetLatestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ConversationMessageDocument>.Filter;
        var filter = filterBuilder.Eq(message => message.OwnerUserId, userId) & filterBuilder.Type(message => message.BriefingDate, BsonType.String) & filterBuilder.Eq(message => message.Role, ConversationMessageDocument.AssistantRole);
        var message = await database.ConversationMessages.Find(filter).SortByDescending(message => message.CreatedAt).ThenByDescending(message => message.Sequence).FirstOrDefaultAsync(cancellationToken);
        return message is { BriefingDate: { } date } ? new MorningBriefingResponse(message.ConversationId, message.RunId, date, message.Text, ResponseMappings.ToUtcOffset(message.CreatedAt)) : null;
    }

    /// <summary>The user's briefing conversation, created when the user has none or deleted it.</summary>
    public async Task<ConversationDocument> EnsureConversationAsync(Guid userId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var existing = await database.Conversations.Find(conversation => conversation.OwnerUserId == userId && conversation.IsBriefing && !conversation.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) return existing;

            var conversation = new ConversationDocument { Id = Guid.CreateVersion7(), Title = ConversationTitle, IsBriefing = true };
            conversation.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
            try
            {
                await database.Conversations.InsertOneAsync(conversation, cancellationToken: cancellationToken);
                return conversation;
            }
            catch (MongoException exception) when (exception.IsDuplicateKey(MongoIndexNames.UniqueBriefingConversation)) { }
        }
    }

    /// <summary>
    /// Appends the messages of one briefing to the conversation while holding its run lock, so the briefing never lands in the middle of an assistant reply.
    /// </summary>
    /// <exception cref="ResourceConflictException">The assistant is answering in the conversation.</exception>
    public async Task AppendAsync(Guid userId, Guid conversationId, Guid runId, IReadOnlyList<ConversationMessageDocument> messages, CancellationToken cancellationToken)
    {
        await conversationService.ClaimRunAsync(userId, conversationId, runId, cancellationToken);
        try { await conversationService.AppendMessagesAsync(userId, conversationId, messages, null, cancellationToken); }
        finally { await conversationService.ReleaseRunAsync(conversationId, runId); }
    }
}
