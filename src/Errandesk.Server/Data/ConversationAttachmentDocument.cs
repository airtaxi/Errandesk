namespace Errandesk.Server.Data;

/// <summary>
/// An image the user uploaded to a conversation. It belongs to a user message once it is sent; until then it expires at <see cref="UnsentExpiresAt"/>.
/// </summary>
public sealed class ConversationAttachmentDocument : DocumentBase
{
    public Guid ConversationId { get; set; }

    /// <summary>The image media type, detected from its content.</summary>
    public string MediaType { get; set; } = "";

    public byte[] Data { get; set; } = [];

    /// <summary>The user message that carries the image, or <see langword="null"/> while it is not sent.</summary>
    public Guid? MessageId { get; set; }

    /// <summary>When an unsent image is removed. Cleared when the image is sent, which keeps it as long as the conversation.</summary>
    public DateTime? UnsentExpiresAt { get; set; }
}
