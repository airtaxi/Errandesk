using Errandesk.Server.Data;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Agent;

/// <summary>
/// A validated assistant run that owns its conversation until it finishes: the new user message and the approval requests it resolves.
/// </summary>
public sealed class AssistantRun(UserDocument user, Guid conversationId, Guid runId, ChatMessage userMessage, string userText, IReadOnlyList<ConversationMessageDocument> resolvedApprovalMessages)
{
    public UserDocument User => user;

    public Guid ConversationId => conversationId;

    /// <summary>ID of the run, used as the ID of the assistant message it produces.</summary>
    public Guid RunId => runId;

    /// <summary>The message sent to the model for the user: text, approval responses, or both.</summary>
    public ChatMessage UserMessage => userMessage;

    /// <summary>The text the user typed, empty for approval decisions.</summary>
    public string UserText => userText;

    /// <summary>Messages whose pending approval requests this run decides, with the decided statuses already applied.</summary>
    public IReadOnlyList<ConversationMessageDocument> ResolvedApprovalMessages => resolvedApprovalMessages;
}
