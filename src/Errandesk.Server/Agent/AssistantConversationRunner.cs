using System.ClientModel;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Agent;

/// <summary>
/// Runs the assistant for one conversation: sends a bounded recent history with the system instruction and the tools to the model, streams the reply as server-sent events,
/// stores every message, and pauses for the user's approval of destructive tools.
/// </summary>
public sealed partial class AssistantConversationRunner(ServerSettings settings, IServiceProvider serviceProvider, ConversationService conversationService, ConversationAttachmentService attachmentService, UserService userService, AgentToolsetFactory toolsetFactory, TimeProvider timeProvider, ILoggerFactory loggerFactory, ILogger<AssistantConversationRunner> logger)
{
    /// <summary>The number of most recent stored messages sent to the model.</summary>
    public const int HistoryMessageLimit = 40;

    /// <summary>The number of most recent images from earlier messages sent to the model. Older images are replaced by <see cref="OmittedImageText"/>.</summary>
    public const int HistoryImageLimit = 20;

    public const string OmittedImageText = "[An earlier image is not included.]";

    public const int MaximumMessageLength = 8000;
    public const int MaximumOutputTokens = 4096;
    public const int MaximumToolRoundTrips = 10;

    private const string DeniedByUserReason = "The user denied this action.";
    private const string DeniedByNewMessageReason = "The user sent a new message instead of deciding.";

    private readonly Lazy<IChatClient> _chatClient = new(() => new ChatClientBuilder(serviceProvider.GetRequiredService<IChatClient>()).UseFunctionInvocation(loggerFactory, client => client.MaximumIterationsPerRequest = MaximumToolRoundTrips).Build(serviceProvider));

    /// <summary>Throws <see cref="AssistantUnavailableException"/> when no AI model is configured.</summary>
    public void EnsureAvailable()
    {
        if (!settings.IsAiAvailable)
        {
            throw new AssistantUnavailableException();
        }
    }

    /// <summary>
    /// Validates a user message and its images and takes the conversation's run lock. The text may be empty when the message carries images.
    /// Pending approval requests are denied by the new message.
    /// </summary>
    public async Task<AssistantRun> StartMessageRunAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var text = request.AttachmentIds is { Count: > 0 } ? InputValidation.OptionalText(request.Text?.Trim(), "message", MaximumMessageLength) : InputValidation.RequireText(request.Text, "message", MaximumMessageLength);
        var attachments = await attachmentService.GetUnsentAsync(userId, conversationId, request.AttachmentIds, cancellationToken);
        return await StartRunAsync(userId, conversationId, text, [.. attachments.Select(attachment => attachment.Id)], pendingMessages =>
        {
            var contents = CreateApprovalResponses(pendingMessages, _ => false, DeniedByNewMessageReason);
            if (text.Length > 0) contents.Add(new TextContent(text));
            contents.AddRange(attachments.Select(attachment => new DataContent(attachment.Data, attachment.MediaType)));
            return contents;
        }, cancellationToken);
    }

    /// <summary>Validates decisions for every pending approval request and takes the conversation's run lock.</summary>
    public async Task<AssistantRun> StartApprovalRunAsync(Guid userId, Guid conversationId, ApprovalDecisionsRequest request, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (request.Decisions is not { Count: > 0 }) throw new RequestValidationException("At least one decision is required.");
        if (request.Decisions.Select(decision => decision.ApprovalId).Distinct(StringComparer.Ordinal).Count() != request.Decisions.Count) throw new RequestValidationException("Each approval request can be decided only once.");
        var decisions = request.Decisions.ToDictionary(decision => decision.ApprovalId, decision => decision.Approved, StringComparer.Ordinal);

        return await StartRunAsync(userId, conversationId, "", [], pendingMessages =>
        {
            var pendingApprovalIds = pendingMessages.SelectMany(message => message.Approvals).Where(approval => approval.Status == ApprovalStatuses.Pending).Select(approval => approval.ApprovalId).ToHashSet(StringComparer.Ordinal);
            if (pendingApprovalIds.Count == 0) throw new ResourceConflictException("There are no pending approval requests.");
            if (decisions.Keys.Any(approvalId => !pendingApprovalIds.Contains(approvalId))) throw new RequestValidationException("A decision names an approval request that is not pending.");
            if (pendingApprovalIds.Any(approvalId => !decisions.ContainsKey(approvalId))) throw new RequestValidationException("Every pending approval request needs a decision.");
            return CreateApprovalResponses(pendingMessages, approval => decisions[approval.ApprovalId], DeniedByUserReason);
        }, cancellationToken);
    }

    /// <summary>Streams the run's events, stores its messages and releases the conversation. Errors after the stream started are reported as an <c>error</c> event.</summary>
    public async Task ExecuteAsync(AssistantRun run, ServerSentEventWriter writer, CancellationToken cancellationToken)
    {
        var jsonContext = ErrandeskJsonSerializerContext.Default;
        var context = new AgentRunContext(run.User.Id, run.ConversationId)
        {
            ToolStarted = data => writer.WriteEventAsync(ServerEventNames.ToolStarted, data, jsonContext.ToolStartedEventData, cancellationToken),
            ToolCompleted = data => writer.WriteEventAsync(ServerEventNames.ToolCompleted, data, jsonContext.ToolCompletedEventData, cancellationToken),
        };
        var toolset = toolsetFactory.Create(context);
        List<ChatResponseUpdate> updates = [];
        List<ApprovalRecordDocument> approvals = [];
        string? errorMessage = null;
        var isCanceled = false;

        try
        {
            var recentMessages = await conversationService.GetRecentMessagesAsync(run.ConversationId, HistoryMessageLimit, cancellationToken);
            foreach (var message in run.ResolvedApprovalMessages) await conversationService.SaveApprovalsAsync(message, cancellationToken);
            var userDocument = new ConversationMessageDocument { Id = Guid.CreateVersion7(), RunId = run.RunId, Role = ConversationMessageDocument.UserRole, Text = run.UserText, ContentsJson = ChatMessageSerialization.SerializeContents([.. run.UserMessage.Contents.Where(content => content is not DataContent)]), AttachmentIds = [.. run.AttachmentIds] };
            await conversationService.AppendMessagesAsync(run.User.Id, run.ConversationId, [userDocument], run.UserText, cancellationToken);
            if (run.AttachmentIds.Count > 0) await attachmentService.MarkSentAsync(run.AttachmentIds, userDocument.Id, cancellationToken);
            var history = await BuildHistoryAsync(run, recentMessages, cancellationToken);

            await writer.WriteEventAsync(ServerEventNames.MessageStarted, new MessageStartedEventData(run.ConversationId, run.RunId), jsonContext.MessageStartedEventData, cancellationToken);
            var options = new ChatOptions { Tools = toolset.CreateTools(), MaxOutputTokens = MaximumOutputTokens };
            await foreach (var update in _chatClient.Value.GetStreamingResponseAsync(history, options, cancellationToken))
            {
                updates.Add(update);
                foreach (var content in update.Contents)
                {
                    if (content is TextContent textContent && update.Role != ChatRole.Tool && !string.IsNullOrEmpty(textContent.Text)) await writer.WriteEventAsync(ServerEventNames.TextDelta, new TextDeltaEventData(textContent.Text), jsonContext.TextDeltaEventData, cancellationToken);
                    else if (content is ToolApprovalRequestContent approvalRequest) approvals.Add(await CreateApprovalRecordAsync(toolset, approvalRequest, writer, cancellationToken));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { isCanceled = true; }
        catch (Exception exception)
        {
            LogRunFailed(logger, exception, run.ConversationId);
            errorMessage = exception is ClientResultException clientResultException ? $"The AI service answered with status {clientResultException.Status}." : "The assistant could not finish the reply.";
        }

        var responseText = "";
        try { responseText = await SaveRunMessagesAsync(run, updates, context.ToolCalls, approvals); }
        catch (Exception exception)
        {
            LogSaveFailed(logger, exception, run.ConversationId);
            errorMessage ??= "The reply could not be saved.";
        }
        finally { await conversationService.ReleaseRunAsync(run.ConversationId, run.RunId); }

        if (isCanceled) return;
        try
        {
            if (errorMessage is not null) await writer.WriteEventAsync(ServerEventNames.Error, new StreamErrorEventData(errorMessage), jsonContext.StreamErrorEventData, cancellationToken);
            else await writer.WriteEventAsync(ServerEventNames.MessageCompleted, new MessageCompletedEventData(run.ConversationId, run.RunId, responseText, approvals.Any(approval => approval.RequiresConfirmation)), jsonContext.MessageCompletedEventData, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<AssistantRun> StartRunAsync(Guid userId, Guid conversationId, string userText, IReadOnlyList<Guid> attachmentIds, Func<List<ConversationMessageDocument>, List<AIContent>> createUserContents, CancellationToken cancellationToken)
    {
        var user = await userService.GetAsync(userId, cancellationToken);
        await conversationService.GetVisibleAsync(userId, conversationId, cancellationToken);
        var runId = Guid.CreateVersion7();
        await conversationService.ClaimRunAsync(userId, conversationId, runId, cancellationToken);
        try
        {
            var pendingMessages = await conversationService.GetPendingApprovalMessagesAsync(conversationId, cancellationToken);
            var contents = createUserContents(pendingMessages);
            return new AssistantRun(user, conversationId, runId, new ChatMessage(ChatRole.User, contents), userText, attachmentIds, pendingMessages);
        }
        catch
        {
            await conversationService.ReleaseRunAsync(conversationId, runId);
            throw;
        }
    }

    /// <summary>
    /// The system instruction followed by the stored messages from the oldest user message within the last <see cref="HistoryMessageLimit"/> messages, and the new user message.
    /// Approval requests stay only in the messages this run decides; tool calls without results are removed.
    /// The <see cref="HistoryImageLimit"/> most recent images of earlier messages are included; older ones are replaced by <see cref="OmittedImageText"/>.
    /// </summary>
    private async Task<List<ChatMessage>> BuildHistoryAsync(AssistantRun run, List<ConversationMessageDocument> recentMessages, CancellationToken cancellationToken)
    {
        var resolvedMessageIds = run.ResolvedApprovalMessages.Select(message => message.Id).ToHashSet();
        var startIndex = Math.Max(0, recentMessages.FindIndex(message => message.Role == ConversationMessageDocument.UserRole && (message.Text.Length > 0 || message.AttachmentIds.Count > 0 || message.BriefingDate is not null)));
        var historyDocuments = recentMessages[startIndex..];
        var historyImageIds = historyDocuments.AsEnumerable().Reverse().SelectMany(document => document.AttachmentIds.AsEnumerable().Reverse()).Take(HistoryImageLimit).ToList();
        var historyImages = await attachmentService.GetManyAsync(run.ConversationId, historyImageIds, cancellationToken);

        List<ChatMessage> conversationMessages = [];
        foreach (var document in historyDocuments)
        {
            var contents = ChatMessageSerialization.DeserializeContents(document.ContentsJson);
            contents.RemoveAll(content => content is ToolApprovalResponseContent || (content is ToolApprovalRequestContent && !resolvedMessageIds.Contains(document.Id)));
            foreach (var attachmentId in document.AttachmentIds) contents.Add(historyImages.TryGetValue(attachmentId, out var image) ? new DataContent(image.Data, image.MediaType) : new TextContent(OmittedImageText));
            if (contents.Count > 0) conversationMessages.Add(new ChatMessage(ToChatRole(document.Role), contents));
        }
        conversationMessages.Add(run.UserMessage);

        return [new ChatMessage(ChatRole.System, AgentSystemPrompt.Create(run.User, timeProvider.GetUtcNow())), .. ChatMessageSerialization.RemoveUnpairedToolContents(conversationMessages)];
    }

    private static async Task<ApprovalRecordDocument> CreateApprovalRecordAsync(AgentToolset toolset, ToolApprovalRequestContent approvalRequest, ServerSentEventWriter writer, CancellationToken cancellationToken)
    {
        var toolCall = approvalRequest.ToolCall as FunctionCallContent;
        var target = toolCall is null ? new AgentApprovalTarget("", null, "") : await toolset.DescribeApprovalTargetAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
        var requiresConfirmation = toolCall is null || AgentToolset.RequiresApproval(toolCall.Name);
        var approval = new ApprovalRecordDocument
        {
            ApprovalId = approvalRequest.RequestId,
            ToolName = toolCall?.Name ?? "",
            EntityType = target.EntityType,
            EntityId = target.EntityId,
            EntityTitle = target.EntityTitle,
            RequiresConfirmation = requiresConfirmation,
            Status = requiresConfirmation ? ApprovalStatuses.Pending : ApprovalStatuses.Approved,
        };
        if (approval.RequiresConfirmation) await writer.WriteEventAsync(ServerEventNames.ApprovalRequested, approval.ToResponse(), ErrandeskJsonSerializerContext.Default.ApprovalRequestResponse, cancellationToken);
        return approval;
    }

    /// <summary>Stores the assistant and tool messages of the run and returns the text of the assistant reply.</summary>
    private async Task<string> SaveRunMessagesAsync(AssistantRun run, List<ChatResponseUpdate> updates, List<ToolCallRecordDocument> toolCalls, List<ApprovalRecordDocument> approvals)
    {
        var messages = ChatMessageSerialization.RemoveUnpairedToolContents(updates.ToChatResponse().Messages);
        List<ConversationMessageDocument> documents = [];
        foreach (var message in messages)
        {
            var resultCallIds = message.Contents.OfType<FunctionResultContent>().Select(result => result.CallId).ToHashSet(StringComparer.Ordinal);
            var approvalIds = message.Contents.OfType<ToolApprovalRequestContent>().Select(request => request.RequestId).ToHashSet(StringComparer.Ordinal);
            documents.Add(new ConversationMessageDocument
            {
                Id = Guid.CreateVersion7(),
                RunId = run.RunId,
                Role = message.Role == ChatRole.Tool ? ConversationMessageDocument.ToolRole : ConversationMessageDocument.AssistantRole,
                Text = message.Role == ChatRole.Tool ? "" : ChatMessageSerialization.GetText(message.Contents),
                ContentsJson = ChatMessageSerialization.SerializeContents(message.Contents),
                ToolCalls = [.. toolCalls.Where(toolCall => resultCallIds.Contains(toolCall.CallId))],
                Approvals = [.. approvals.Where(approval => approvalIds.Contains(approval.ApprovalId))],
            });
        }

        await conversationService.AppendMessagesAsync(run.User.Id, run.ConversationId, documents, null, CancellationToken.None);
        return string.Join("\n\n", documents.Where(document => document.Role == ConversationMessageDocument.AssistantRole && document.Text.Length > 0).Select(document => document.Text));
    }

    /// <summary>Answers every approval request of the messages: tools that need no confirmation are approved; the others as <paramref name="isApproved"/> decides. Updates the stored statuses.</summary>
    private static List<AIContent> CreateApprovalResponses(IEnumerable<ConversationMessageDocument> messages, Func<ApprovalRecordDocument, bool> isApproved, string denialReason)
    {
        List<AIContent> responses = [];
        foreach (var message in messages)
        {
            foreach (var approvalRequest in ChatMessageSerialization.DeserializeContents(message.ContentsJson).OfType<ToolApprovalRequestContent>())
            {
                var approval = message.Approvals.Find(record => record.ApprovalId == approvalRequest.RequestId);
                var isRequestApproved = approval is not null && (!approval.RequiresConfirmation || isApproved(approval));
                if (approval is { Status: ApprovalStatuses.Pending }) approval.Status = isRequestApproved ? ApprovalStatuses.Approved : ApprovalStatuses.Denied;
                responses.Add(approvalRequest.CreateResponse(isRequestApproved, isRequestApproved ? null : denialReason));
            }
        }
        return responses;
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        ConversationMessageDocument.UserRole => ChatRole.User,
        ConversationMessageDocument.ToolRole => ChatRole.Tool,
        _ => ChatRole.Assistant,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "The assistant run in conversation {ConversationId} failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The messages of the assistant run in conversation {ConversationId} could not be saved.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, Guid conversationId);
}
