using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A destructive change the assistant asks the user to approve or deny.
/// </summary>
public sealed partial class ApprovalModel : ObservableObject
{
    public ApprovalModel(ApprovalRequestResponse response)
    {
        ApprovalId = response.ApprovalId;
        PromptText = AssistantTextHelper.GetApprovalPrompt(response.ToolName, response.EntityType, response.EntityTitle);
        Status = response.Status;
    }

    public string ApprovalId { get; }

    public string PromptText { get; }

    /// <summary>
    /// One of <see cref="ApprovalStatuses"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsDecided), nameof(StatusText), nameof(AreDecisionButtonsVisible))]
    public partial string Status { get; set; } = ApprovalStatuses.Pending;

    /// <summary>
    /// The user's choice while other approval requests of the same reply still wait for a decision.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsDecided), nameof(AreDecisionButtonsVisible))]
    public partial bool? ChosenDecision { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreDecisionButtonsVisible))]
    public partial bool IsSubmitting { get; set; }

    public bool IsPending => Status == ApprovalStatuses.Pending;

    public bool AreDecisionButtonsVisible => IsPending && ChosenDecision is null && !IsSubmitting;

    public bool IsDecided => !AreDecisionButtonsVisible;

    public string StatusText => (Status, ChosenDecision) switch
    {
        (ApprovalStatuses.Approved, _) or (ApprovalStatuses.Pending, true) => LocalizedStrings.Get("ApprovalApprovedText"),
        (ApprovalStatuses.Denied, _) or (ApprovalStatuses.Pending, false) => LocalizedStrings.Get("ApprovalDeniedText"),
        _ => string.Empty,
    };

    /// <summary>
    /// The question, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => PromptText;
}
