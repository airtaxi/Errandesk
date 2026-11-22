using Errandesk.Contracts;
using Errandesk.Server.Data;

namespace Errandesk.Server.Agent;

/// <summary>
/// The user and conversation an assistant run acts for, the observers of its tool activity, and the tool calls it made.
/// </summary>
public sealed class AgentRunContext(Guid userId, Guid? conversationId)
{
    public Guid UserId => userId;

    public Guid? ConversationId => conversationId;

    /// <summary>Called when a tool starts.</summary>
    public Func<ToolStartedEventData, Task>? ToolStarted { get; init; }

    /// <summary>Called when a tool finishes, successfully or not.</summary>
    public Func<ToolCompletedEventData, Task>? ToolCompleted { get; init; }

    /// <summary>Every tool call of the run in invocation order.</summary>
    public List<ToolCallRecordDocument> ToolCalls { get; } = [];

    public async Task NotifyToolStartedAsync(ToolStartedEventData data)
    {
        if (ToolStarted is not null)
        {
            await ToolStarted(data);
        }
    }

    public async Task NotifyToolCompletedAsync(ToolCompletedEventData data)
    {
        lock (ToolCalls) ToolCalls.Add(new ToolCallRecordDocument { CallId = data.CallId, ToolName = data.ToolName, Succeeded = data.Succeeded, AgentActionId = data.AgentActionId });
        if (ToolCompleted is not null) await ToolCompleted(data);
    }
}
