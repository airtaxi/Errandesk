namespace Errandesk.Server.Agent;

/// <summary>
/// A morning briefing that was written and stored.
/// </summary>
/// <param name="ConversationId">The briefing conversation that holds it.</param>
/// <param name="MessageId">ID of the briefing in the conversation's messages.</param>
/// <param name="Text">The briefing as Markdown.</param>
public sealed record WrittenMorningBriefing(Guid ConversationId, Guid MessageId, string Text);
