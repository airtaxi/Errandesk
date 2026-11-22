namespace Errandesk.Contracts;

/// <summary>
/// Decides every pending approval request of a conversation and resumes the assistant.
/// </summary>
/// <param name="Decisions">One decision for each pending approval request.</param>
public sealed record ApprovalDecisionsRequest(IReadOnlyList<ApprovalDecision> Decisions);
