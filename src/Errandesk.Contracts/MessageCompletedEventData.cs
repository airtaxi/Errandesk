namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.MessageCompleted"/> event.
/// </summary>
/// <param name="ConversationId">Conversation of the reply.</param>
/// <param name="MessageId">ID of the assistant message.</param>
/// <param name="Text">The complete text of the assistant message.</param>
/// <param name="HasPendingApprovals">Whether the reply waits for approval decisions.</param>
public sealed record MessageCompletedEventData(Guid ConversationId, Guid MessageId, string Text, bool HasPendingApprovals);
