using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A tool the assistant ran for a reply, with the change it made and whether that change was undone.
/// </summary>
public sealed partial class ToolActivityModel : ObservableObject
{
    public ToolActivityModel(string callId, string toolName)
    {
        CallId = callId;
        ToolName = toolName;
        DisplayText = AssistantTextHelper.GetToolText(toolName);
        IsRunning = true;
    }

    public ToolActivityModel(ToolActivityResponse response) : this(response.CallId, response.ToolName)
    {
        Complete(response.Succeeded, response.AgentActionId, null);
        IsUndone = response.IsUndone;
    }

    public string CallId { get; }

    public string ToolName { get; }

    public string DisplayText { get; }

    public Guid? AgentActionId { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSucceededIconVisible), nameof(IsFailedIconVisible), nameof(CanUndo))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSucceededIconVisible), nameof(IsFailedIconVisible), nameof(CanUndo))]
    public partial bool IsSucceeded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsUndone { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsUndoing { get; set; }

    /// <summary>
    /// Whether the server refused to undo the change because the item changed after it or the change was already undone.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsUndoUnavailable { get; set; }

    public bool IsSucceededIconVisible => !IsRunning && IsSucceeded;

    public bool IsFailedIconVisible => !IsRunning && !IsSucceeded;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// Whether the tool changed data that can still be restored.
    /// </summary>
    public bool CanUndo => !IsRunning && IsSucceeded && AgentActionId is not null && !IsUndone && !IsUndoing && !IsUndoUnavailable;

    /// <summary>
    /// The description, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => DisplayText;

    public void Complete(bool isSucceeded, Guid? agentActionId, string? error)
    {
        AgentActionId = agentActionId;
        IsSucceeded = isSucceeded;
        ErrorText = isSucceeded ? string.Empty : error ?? string.Empty;
        IsRunning = false;
    }
}
