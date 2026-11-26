namespace Errandesk.Client.Models;

/// <summary>
/// An image the user attached to a sent message. The thumbnail is set once the image is loaded.
/// </summary>
public sealed partial class MessageImageModel(Guid attachmentId) : ObservableObject
{
    public Guid AttachmentId => attachmentId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    public partial bool HasFailed { get; set; }

    /// <summary>Whether the image could not be loaded, which shows a placeholder icon.</summary>
    public bool IsPlaceholderVisible => HasFailed && Thumbnail is null;
}
