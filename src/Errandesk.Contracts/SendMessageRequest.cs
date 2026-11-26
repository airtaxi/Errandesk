namespace Errandesk.Contracts;

/// <summary>
/// A message from the user to the assistant. Approval requests that are still pending are denied.
/// </summary>
/// <param name="Text">Message text. May be empty when the message carries images.</param>
/// <param name="AttachmentIds">Images uploaded to the conversation and not sent yet, at most <see cref="ConversationAttachmentLimits.MaximumCount"/>, in the order they are shown.</param>
public sealed record SendMessageRequest(string Text, IReadOnlyList<Guid>? AttachmentIds = null);
