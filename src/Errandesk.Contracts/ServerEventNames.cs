namespace Errandesk.Contracts;

/// <summary>
/// Event names of the server-sent event streams.
/// </summary>
public static class ServerEventNames
{
    /// <summary>An assistant reply starts. Data: <see cref="MessageStartedEventData"/>.</summary>
    public const string MessageStarted = "message.started";

    /// <summary>More text of the assistant reply. Data: <see cref="TextDeltaEventData"/>.</summary>
    public const string TextDelta = "text.delta";

    /// <summary>The assistant started a tool. Data: <see cref="ToolStartedEventData"/>.</summary>
    public const string ToolStarted = "tool.started";

    /// <summary>A tool finished. Data: <see cref="ToolCompletedEventData"/>.</summary>
    public const string ToolCompleted = "tool.completed";

    /// <summary>The assistant waits for the user to approve or deny a destructive tool. Data: <see cref="ApprovalRequestResponse"/>.</summary>
    public const string ApprovalRequested = "approval.requested";

    /// <summary>The assistant reply is complete. Data: <see cref="MessageCompletedEventData"/>.</summary>
    public const string MessageCompleted = "message.completed";

    /// <summary>The run failed. Data: <see cref="StreamErrorEventData"/>.</summary>
    public const string Error = "error";

    /// <summary>Data the user can see changed. Data: <see cref="ChangedEventData"/>.</summary>
    public const string Changed = "changed";
}
