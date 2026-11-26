namespace Errandesk.Contracts;

/// <summary>
/// Limits on the images attached to an assistant message.
/// </summary>
public static class ConversationAttachmentLimits
{
    /// <summary>The most images one message can carry.</summary>
    public const int MaximumCount = 10;

    /// <summary>The largest accepted image, in bytes.</summary>
    public const int MaximumLength = 5 * 1024 * 1024;
}
