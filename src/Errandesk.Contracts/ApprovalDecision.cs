namespace Errandesk.Contracts;

/// <summary>
/// The user's decision on one approval request.
/// </summary>
/// <param name="ApprovalId">ID from <see cref="ApprovalRequestResponse.ApprovalId"/>.</param>
/// <param name="Approved">Whether the tool may run.</param>
public sealed record ApprovalDecision(string ApprovalId, bool Approved);
