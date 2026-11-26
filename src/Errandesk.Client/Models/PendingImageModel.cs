namespace Errandesk.Client.Models;

/// <summary>
/// An image in the message box waiting to be sent, with the attachment it was uploaded as when an earlier send attempt got that far.
/// </summary>
public sealed class PendingImageModel(EncodedImage image, ImageSource thumbnail)
{
    public EncodedImage Image => image;

    public ImageSource Thumbnail => thumbnail;

    /// <summary>The conversation the image was uploaded to, or <see langword="null"/> while it is not uploaded.</summary>
    public Guid? UploadedConversationId { get; private set; }

    /// <summary>The attachment ID of the uploaded image.</summary>
    public Guid? UploadedAttachmentId { get; private set; }

    /// <summary>The attachment ID when the image was already uploaded to <paramref name="conversationId"/>.</summary>
    public Guid? GetAttachmentId(Guid conversationId) => UploadedConversationId == conversationId ? UploadedAttachmentId : null;

    public void SetUploaded(Guid conversationId, Guid attachmentId)
    {
        UploadedConversationId = conversationId;
        UploadedAttachmentId = attachmentId;
    }
}
