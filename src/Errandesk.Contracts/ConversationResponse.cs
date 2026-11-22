namespace Errandesk.Contracts;

/// <summary>
/// An assistant conversation.
/// </summary>
/// <param name="Id">Conversation ID.</param>
/// <param name="Title">Title, taken from the first message when none was given. Empty for a new conversation.</param>
/// <param name="HasPendingApprovals">Whether the assistant waits for approval decisions.</param>
/// <param name="CreatedAt">When the conversation was created.</param>
/// <param name="UpdatedAt">When the conversation last changed.</param>
/// <param name="IsBriefing">Whether this is the conversation that collects the user's morning briefings.</param>
public sealed record ConversationResponse(Guid Id, string Title, bool HasPendingApprovals, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsBriefing = false);
