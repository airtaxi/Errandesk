namespace Errandesk.Contracts;

/// <summary>
/// A message of an assistant conversation.
/// </summary>
/// <param name="Id">Message ID. For assistant messages it equals <see cref="MessageStartedEventData.MessageId"/> of the stream that produced it.</param>
/// <param name="Role">One of <see cref="ConversationRoles"/>.</param>
/// <param name="Text">Message text. Markdown for assistant messages.</param>
/// <param name="ToolActivities">Tools the assistant ran for this message.</param>
/// <param name="Approvals">Approval requests of this message.</param>
/// <param name="CreatedAt">When the message was created.</param>
/// <param name="AttachmentIds">Images the user attached to the message, in order. Empty or <see langword="null"/> for messages without images.</param>
public sealed record ConversationMessageResponse(Guid Id, string Role, string Text, IReadOnlyList<ToolActivityResponse> ToolActivities, IReadOnlyList<ApprovalRequestResponse> Approvals, DateTimeOffset CreatedAt, IReadOnlyList<Guid>? AttachmentIds = null);
