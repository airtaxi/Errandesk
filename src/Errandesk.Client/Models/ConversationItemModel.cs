using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// An assistant conversation as shown in the conversation list.
/// </summary>
public sealed partial class ConversationItemModel : ObservableObject
{
    public ConversationItemModel(ConversationResponse response)
    {
        Id = response.Id;
        IsBriefing = response.IsBriefing;
        Apply(response);
    }

    public Guid Id { get; }

    /// <summary>
    /// Whether the conversation collects the morning briefings; it is shown with a localized title.
    /// </summary>
    public bool IsBriefing { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    public partial DateTimeOffset UpdatedAt { get; set; }

    [ObservableProperty]
    public partial bool HasPendingApprovals { get; set; }

    public string DisplayTitle => IsBriefing ? LocalizedStrings.Get("MorningBriefingTitle") : Title.Length > 0 ? Title : LocalizedStrings.Get("NewConversationTitle");

    public string UpdatedText => DateDisplayHelper.FormatShortDate(UpdatedAt);

    /// <summary>
    /// The title, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => DisplayTitle;

    public void Apply(ConversationResponse response)
    {
        Title = response.Title;
        UpdatedAt = response.UpdatedAt;
        HasPendingApprovals = response.HasPendingApprovals;
    }
}
