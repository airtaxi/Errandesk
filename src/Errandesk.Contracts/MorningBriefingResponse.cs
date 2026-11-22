namespace Errandesk.Contracts;

/// <summary>
/// A morning briefing written by the assistant.
/// </summary>
/// <param name="ConversationId">The briefing conversation that holds the briefing.</param>
/// <param name="MessageId">ID of the briefing in the conversation's messages.</param>
/// <param name="Date">The user's local date the briefing was written for.</param>
/// <param name="Text">The briefing as Markdown.</param>
/// <param name="CreatedAt">When the briefing was written.</param>
public sealed record MorningBriefingResponse(Guid ConversationId, Guid MessageId, DateOnly Date, string Text, DateTimeOffset CreatedAt);
