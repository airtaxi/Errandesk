namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.MessageStarted"/> event.
/// </summary>
/// <param name="ConversationId">Conversation of the reply.</param>
/// <param name="MessageId">ID of the assistant message, the same as in the conversation history.</param>
public sealed record MessageStartedEventData(Guid ConversationId, Guid MessageId);
