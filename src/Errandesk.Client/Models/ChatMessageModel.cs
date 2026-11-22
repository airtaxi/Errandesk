using System.Collections.ObjectModel;
using System.Text;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A message in an assistant conversation: the user's text, or the assistant's reply with the tools it ran and the approvals it asks for.
/// While a reply streams in, its text is shown at most every <see cref="s_textRenderInterval"/>, because the Markdown view rebuilds on each change.
/// </summary>
public sealed partial class ChatMessageModel : ObservableObject
{
    private static readonly TimeSpan s_textRenderInterval = TimeSpan.FromMilliseconds(100);

    private readonly StringBuilder _streamedText = new();
    private bool _isTextRenderScheduled;

    public ChatMessageModel(Guid id, bool isUser, string text)
    {
        Id = id;
        IsUser = isUser;
        Text = text;
        _streamedText.Append(text);
    }

    public ChatMessageModel(ConversationMessageResponse response) : this(response.Id, response.Role == ConversationRoles.User, response.Text)
    {
        foreach (var toolActivity in response.ToolActivities) ToolActivities.Add(new ToolActivityModel(toolActivity));
        foreach (var approval in response.Approvals) Approvals.Add(new ApprovalModel(approval));
    }

    public Guid Id { get; }

    public bool IsUser { get; }

    public bool IsAssistant => !IsUser;

    public ObservableCollection<ToolActivityModel> ToolActivities { get; } = [];

    public ObservableCollection<ApprovalModel> Approvals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStopped { get; set; }

    public bool HasText => Text.Length > 0;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// The text, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => Text;

    /// <summary>
    /// Adds streamed text. The shown text catches up after a short interval.
    /// </summary>
    public void AppendText(string text)
    {
        _streamedText.Append(text);
        if (_isTextRenderScheduled) return;
        _isTextRenderScheduled = true;
        _ = RenderTextAfterIntervalAsync();
    }

    /// <summary>
    /// Shows the complete text of the reply.
    /// </summary>
    public void Complete(string text)
    {
        _streamedText.Clear().Append(text);
        Text = text;
        IsStreaming = false;
    }

    /// <summary>
    /// Shows the text streamed so far and ends streaming, after the reply was stopped or failed.
    /// </summary>
    public void EndStreaming()
    {
        Text = _streamedText.ToString();
        IsStreaming = false;
    }

    public ToolActivityModel? FindToolActivity(string callId) => ToolActivities.FirstOrDefault(toolActivity => toolActivity.CallId == callId);

    private async Task RenderTextAfterIntervalAsync()
    {
        await Task.Delay(s_textRenderInterval);
        _isTextRenderScheduled = false;
        Text = _streamedText.ToString();
    }
}
