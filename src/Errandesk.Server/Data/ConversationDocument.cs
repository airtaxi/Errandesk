namespace Errandesk.Server.Data;

/// <summary>
/// An assistant conversation. Its messages are stored in <c>conversation_messages</c>.
/// </summary>
public sealed class ConversationDocument : DocumentBase
{
    public string Title { get; set; } = "";

    /// <summary>The highest <see cref="ConversationMessageDocument.Sequence"/> handed out so far.</summary>
    public long LastMessageSequence { get; set; }

    /// <summary>The assistant run that currently owns the conversation, or <see langword="null"/> when none runs.</summary>
    public Guid? ActiveRunId { get; set; }

    /// <summary>When <see cref="ActiveRunId"/> started. Runs older than the run timeout no longer block new runs.</summary>
    public DateTime? ActiveRunStartedAt { get; set; }

    /// <summary>Whether the conversation collects the user's morning briefings. Each user has at most one that is not deleted.</summary>
    public bool IsBriefing { get; set; }
}
