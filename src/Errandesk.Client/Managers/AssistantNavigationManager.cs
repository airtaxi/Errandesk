namespace Errandesk.Client.Managers;

/// <summary>
/// Lets pages outside the assistant pane, such as My Day with its morning briefing, open a conversation in the assistant pane.
/// </summary>
public sealed class AssistantNavigationManager
{
    /// <summary>
    /// Raised with the ID of the conversation to open. The shell handles it by opening the assistant pane with that conversation.
    /// </summary>
    public event EventHandler<Guid>? ConversationOpenRequested;

    public void OpenConversation(Guid conversationId) => ConversationOpenRequested?.Invoke(this, conversationId);
}
