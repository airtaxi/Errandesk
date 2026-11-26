using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Stores assistant conversations and their messages, hands out the run lock of a conversation, and builds the conversation history shown to the user.
/// </summary>
public sealed class ConversationService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, AgentActionService agentActionService, DocumentUpdater documentUpdater)
{
    public const int MaximumTitleLength = 200;

    /// <summary>How long a run may hold the conversation before another run may take it over.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);

    private const string ResourceName = "conversation";

    /// <summary>The user's conversations, most recently changed first.</summary>
    public async Task<IReadOnlyList<ConversationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversations = await database.Conversations.Find(conversation => conversation.OwnerUserId == userId && !conversation.IsDeleted).SortByDescending(conversation => conversation.UpdatedAt).ThenByDescending(conversation => conversation.Id).ToListAsync(cancellationToken);
        var pendingFilter = Builders<ConversationMessageDocument>.Filter.Where(message => message.OwnerUserId == userId) & PendingApprovalFilter();
        var conversationsWithPendingApprovals = (await database.ConversationMessages.Distinct(message => message.ConversationId, pendingFilter).ToListAsync(cancellationToken)).ToHashSet();
        return [.. conversations.Select(conversation => conversation.ToResponse(conversationsWithPendingApprovals.Contains(conversation.Id)))];
    }

    public async Task<ConversationResponse> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await GetVisibleAsync(userId, conversationId, cancellationToken);
        return conversation.ToResponse((await GetPendingApprovalMessagesAsync(conversationId, cancellationToken)).Count > 0);
    }

    public async Task<CreateResult<ConversationResponse>> CreateAsync(Guid userId, CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var title = InputValidation.OptionalText(request.Title?.Trim(), "title", MaximumTitleLength);
        var conversation = new ConversationDocument { Id = InputValidation.ClientId(request.Id), Title = title };
        var result = await documentUpdater.InsertIdempotentAsync(database.Conversations, conversation, userId, ResourceName, cancellationToken);
        return new CreateResult<ConversationResponse>(result.Resource.ToResponse(false), result.IsCreated);
    }

    /// <summary>Deletes the conversation, keeping a tombstone, and removes its messages and images.</summary>
    public async Task DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        await documentUpdater.SoftDeleteAsync(database.Conversations, VisibleFilter(userId, conversationId), ResourceName, cancellationToken);
        await database.ConversationMessages.DeleteManyAsync(message => message.ConversationId == conversationId && message.OwnerUserId == userId, cancellationToken);
        await database.ConversationAttachments.DeleteManyAsync(attachment => attachment.ConversationId == conversationId && attachment.OwnerUserId == userId, cancellationToken);
    }

    public async Task<ConversationDocument> GetVisibleAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) => await database.Conversations.Find(VisibleFilter(userId, conversationId)).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    /// <summary>
    /// The messages shown to the user, oldest first: each user message with its images, and one assistant message per run with its text, tool activity and approval requests.
    /// </summary>
    public async Task<IReadOnlyList<ConversationMessageResponse>> GetMessagesAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        await GetVisibleAsync(userId, conversationId, cancellationToken);
        var documents = await database.ConversationMessages.Find(message => message.ConversationId == conversationId && message.OwnerUserId == userId).SortBy(message => message.Sequence).ToListAsync(cancellationToken);
        var agentActionIds = documents.SelectMany(document => document.ToolCalls).Select(toolCall => toolCall.AgentActionId).OfType<Guid>().ToList();
        var undoneAgentActionIds = await agentActionService.GetUndoneIdsAsync(userId, agentActionIds, cancellationToken);

        List<ConversationMessageResponse> messages = [];
        List<ConversationMessageDocument> runDocuments = [];
        void FlushRun()
        {
            if (runDocuments.Count == 0) return;
            var text = string.Join("\n\n", runDocuments.Where(document => document.Role == ConversationMessageDocument.AssistantRole && document.Text.Length > 0).Select(document => document.Text));
            var toolActivities = runDocuments.SelectMany(document => document.ToolCalls).Select(toolCall => new ToolActivityResponse(toolCall.CallId, toolCall.ToolName, toolCall.Succeeded, toolCall.AgentActionId, toolCall.AgentActionId is { } actionId && undoneAgentActionIds.Contains(actionId))).ToList();
            var approvals = runDocuments.SelectMany(document => document.Approvals).Where(approval => approval.RequiresConfirmation).Select(approval => approval.ToResponse()).ToList();
            if (text.Length > 0 || toolActivities.Count > 0 || approvals.Count > 0) messages.Add(new ConversationMessageResponse(runDocuments[0].RunId, ConversationRoles.Assistant, text, toolActivities, approvals, ResponseMappings.ToUtcOffset(runDocuments[0].CreatedAt)));
            runDocuments.Clear();
        }

        foreach (var document in documents)
        {
            if (document.Role == ConversationMessageDocument.UserRole)
            {
                if (document.Text.Length == 0 && document.AttachmentIds.Count == 0) continue;
                FlushRun();
                messages.Add(new ConversationMessageResponse(document.Id, ConversationRoles.User, document.Text, [], [], ResponseMappings.ToUtcOffset(document.CreatedAt), document.AttachmentIds));
                continue;
            }

            if (runDocuments.Count > 0 && runDocuments[0].RunId != document.RunId) FlushRun();
            runDocuments.Add(document);
        }
        FlushRun();
        return messages;
    }

    /// <summary>The most recent messages of the conversation, oldest first.</summary>
    public async Task<List<ConversationMessageDocument>> GetRecentMessagesAsync(Guid conversationId, int limit, CancellationToken cancellationToken)
    {
        var documents = await database.ConversationMessages.Find(message => message.ConversationId == conversationId).SortByDescending(message => message.Sequence).Limit(limit).ToListAsync(cancellationToken);
        documents.Reverse();
        return documents;
    }

    /// <summary>Messages of the conversation that carry approval requests the user has not decided yet.</summary>
    public Task<List<ConversationMessageDocument>> GetPendingApprovalMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => database.ConversationMessages.Find(Builders<ConversationMessageDocument>.Filter.Eq(message => message.ConversationId, conversationId) & PendingApprovalFilter()).SortBy(message => message.Sequence).ToListAsync(cancellationToken);

    /// <summary>Lets <paramref name="runId"/> own the conversation unless another run started less than <see cref="RunTimeout"/> ago.</summary>
    public async Task ClaimRunAsync(Guid userId, Guid conversationId, Guid runId, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var staleRunStart = utcNow - RunTimeout;
        var filter = VisibleFilter(userId, conversationId) & Builders<ConversationDocument>.Filter.Where(conversation => conversation.ActiveRunId == null || conversation.ActiveRunStartedAt < staleRunStart);
        var update = Builders<ConversationDocument>.Update.Set(conversation => conversation.ActiveRunId, runId).Set(conversation => conversation.ActiveRunStartedAt, utcNow);
        var result = await database.Conversations.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0) throw new ResourceConflictException("The assistant is still answering in this conversation.");
    }

    public Task ReleaseRunAsync(Guid conversationId, Guid runId) => database.Conversations.UpdateOneAsync(conversation => conversation.Id == conversationId && conversation.ActiveRunId == runId, Builders<ConversationDocument>.Update.Set(conversation => conversation.ActiveRunId, null).Set(conversation => conversation.ActiveRunStartedAt, null), cancellationToken: CancellationToken.None);

    /// <summary>
    /// Appends messages in the given order, assigning sequence numbers and the base fields, and marks the conversation as changed.
    /// Sets the title from <paramref name="titleCandidate"/> when the conversation has none.
    /// </summary>
    public async Task AppendMessagesAsync(Guid userId, Guid conversationId, IReadOnlyList<ConversationMessageDocument> messages, string? titleCandidate, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var update = Builders<ConversationDocument>.Update.Inc(conversation => conversation.LastMessageSequence, (long)messages.Count).Set(conversation => conversation.UpdatedAt, utcNow).Set(conversation => conversation.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        var conversation = await database.Conversations.FindOneAndUpdateAsync(VisibleFilter(userId, conversationId), update, new FindOneAndUpdateOptions<ConversationDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

        if (!string.IsNullOrWhiteSpace(titleCandidate) && conversation.Title.Length == 0)
        {
            var title = CreateTitle(titleCandidate);
            await database.Conversations.UpdateOneAsync(stored => stored.Id == conversationId && stored.Title == "", Builders<ConversationDocument>.Update.Set(stored => stored.Title, title), cancellationToken: cancellationToken);
        }

        if (messages.Count == 0) return;
        var sequence = conversation.LastMessageSequence - messages.Count;
        foreach (var message in messages)
        {
            message.ConversationId = conversationId;
            message.Sequence = ++sequence;
            message.StampCreated(userId, utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
        }
        await database.ConversationMessages.InsertManyAsync(messages, cancellationToken: cancellationToken);
    }

    /// <summary>Stores the user's decisions on the approval requests of a message.</summary>
    public async Task SaveApprovalsAsync(ConversationMessageDocument message, CancellationToken cancellationToken)
    {
        var update = Builders<ConversationMessageDocument>.Update.Set(stored => stored.Approvals, message.Approvals).Set(stored => stored.UpdatedAt, documentUpdater.GetUtcNow()).Set(stored => stored.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.ConversationMessages.UpdateOneAsync(stored => stored.Id == message.Id, update, cancellationToken: cancellationToken);
    }

    /// <summary>The first line of <paramref name="text"/>, shortened to fit a title.</summary>
    public static string CreateTitle(string text)
    {
        const int MaximumGeneratedTitleLength = 80;
        var firstLine = text.Trim().Split('\n', 2)[0].Trim();
        return firstLine.Length <= MaximumGeneratedTitleLength ? firstLine : $"{firstLine[..(MaximumGeneratedTitleLength - 1)].TrimEnd()}…";
    }

    private static FilterDefinition<ConversationMessageDocument> PendingApprovalFilter() => Builders<ConversationMessageDocument>.Filter.ElemMatch(message => message.Approvals, approval => approval.Status == ApprovalStatuses.Pending);

    private static FilterDefinition<ConversationDocument> VisibleFilter(Guid userId, Guid conversationId) => Builders<ConversationDocument>.Filter.Where(conversation => conversation.Id == conversationId && conversation.OwnerUserId == userId && !conversation.IsDeleted);
}
