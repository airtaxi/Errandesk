namespace Errandesk.Contracts;

/// <summary>
/// An image uploaded for an assistant message.
/// </summary>
/// <param name="Id">Attachment ID, passed in <see cref="SendMessageRequest.AttachmentIds"/>.</param>
/// <param name="MediaType">The image media type: <c>image/jpeg</c>, <c>image/png</c>, <c>image/gif</c> or <c>image/webp</c>.</param>
/// <param name="Length">Size of the image in bytes.</param>
/// <param name="CreatedAt">When the image was uploaded.</param>
public sealed record ConversationAttachmentResponse(Guid Id, string MediaType, long Length, DateTimeOffset CreatedAt);
