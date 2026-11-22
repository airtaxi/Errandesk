namespace Errandesk.Server.Data;

/// <summary>
/// The morning briefing settings of a user and the state of the briefing scheduler for that user.
/// </summary>
public sealed class MorningBriefingSettingsDocument
{
    public bool IsEnabled { get; set; }

    /// <summary>Local time of the briefing in the user's time zone, as <c>HH:mm</c>.</summary>
    public string Time { get; set; } = "";

    /// <summary>The user's local date of the last briefing written, so each local day gets at most one briefing.</summary>
    public DateOnly? LastBriefingDate { get; set; }

    /// <summary>When a scheduler claimed today's briefing. A claim older than the claim timeout is treated as abandoned.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Identifies the scheduler run that holds the claim.</summary>
    public Guid? ClaimId { get; set; }
}
