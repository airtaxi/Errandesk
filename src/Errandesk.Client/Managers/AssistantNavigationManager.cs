namespace Errandesk.Client.Managers;

/// <summary>
/// Lets pages outside the assistant pane, such as My Day with its morning briefing or the home screen widget, open the assistant pane,
/// and remembers whether the signed-in server runs the assistant.
/// </summary>
/// <remarks>Call every member on the UI thread.</remarks>
public sealed class AssistantNavigationManager
{
    private bool _isAssistantOpenPending;

    /// <summary>
    /// Raised with the ID of the conversation to open. The shell handles it by opening the assistant pane with that conversation.
    /// </summary>
    public event EventHandler<Guid>? ConversationOpenRequested;

    /// <summary>
    /// Raised when a request to open the assistant pane is waiting. The shell handles it with <see cref="TryTakePendingAssistantRequest"/>.
    /// </summary>
    public event EventHandler? AssistantOpenRequested;

    /// <summary>
    /// Raised when <see cref="IsAssistantAvailable"/> changed.
    /// </summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Whether the signed-in server runs the assistant, as its server information last said; <see langword="null"/> before it was read.
    /// </summary>
    public bool? IsAssistantAvailable { get; private set; }

    public void OpenConversation(Guid conversationId) => ConversationOpenRequested?.Invoke(this, conversationId);

    /// <summary>
    /// Asks the shell to open the assistant pane. A request made before the shell is shown waits until the shell takes it.
    /// </summary>
    public void OpenAssistant()
    {
        _isAssistantOpenPending = true;
        AssistantOpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool TryTakePendingAssistantRequest()
    {
        var isPending = _isAssistantOpenPending;
        _isAssistantOpenPending = false;
        return isPending;
    }

    /// <summary>
    /// Records whether the signed-in server runs the assistant, from server information read for that server.
    /// </summary>
    public void ReportAvailability(bool isAvailable)
    {
        if (IsAssistantAvailable == isAvailable) return;
        IsAssistantAvailable = isAvailable;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops a waiting request, for example when the user signs out.
    /// </summary>
    public void Clear() => _isAssistantOpenPending = false;
}
