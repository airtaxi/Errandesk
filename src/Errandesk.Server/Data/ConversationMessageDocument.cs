namespace Errandesk.Server.Data;

/// <summary>
/// One chat message of a conversation as exchanged with the model: a user message, an assistant message, or tool results.
/// </summary>
public sealed class ConversationMessageDocument : DocumentBase
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
    public const string ToolRole = "tool";

    public Guid ConversationId { get; set; }

    /// <summary>The assistant run that produced the message. User messages carry the run they started.</summary>
    public Guid RunId { get; set; }

    /// <summary>Position in the conversation, ascending and unique per conversation.</summary>
    public long Sequence { get; set; }

    /// <summary><see cref="UserRole"/>, <see cref="AssistantRole"/> or <see cref="ToolRole"/>.</summary>
    public string Role { get; set; } = "";

    /// <summary>The text content shown to the user. Empty for messages without text.</summary>
    public string Text { get; set; } = "";

    /// <summary>The message contents in the JSON form of the chat abstractions, including tool calls, tool results and approval requests.</summary>
    public string ContentsJson { get; set; } = "[]";

    /// <summary>Tools whose results this message carries.</summary>
    public List<ToolCallRecordDocument> ToolCalls { get; set; } = [];

    /// <summary>Approval requests this message carries.</summary>
    public List<ApprovalRecordDocument> Approvals { get; set; } = [];

    /// <summary>
    /// The user's local date of the morning briefing this message belongs to: the hidden request with the day's data, or the briefing written by the model.
    /// <see langword="null"/> for every other message.
    /// </summary>
    public DateOnly? BriefingDate { get; set; }
}
