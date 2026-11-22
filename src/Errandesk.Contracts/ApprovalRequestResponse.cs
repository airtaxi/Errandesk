namespace Errandesk.Contracts;

/// <summary>
/// A destructive tool call that needs the user's approval before it runs.
/// </summary>
/// <param name="ApprovalId">ID used in <see cref="ApprovalDecision"/>.</param>
/// <param name="ToolName">Name of the tool, such as <c>delete_task</c>.</param>
/// <param name="EntityType">Type of the affected item, one of <see cref="EntityTypes"/>.</param>
/// <param name="EntityId">ID of the affected item, or <see langword="null"/> when the tool call named no valid ID.</param>
/// <param name="EntityTitle">Title or name of the affected item, empty when it was not found.</param>
/// <param name="Status">One of <see cref="ApprovalStatuses"/>.</param>
public sealed record ApprovalRequestResponse(string ApprovalId, string ToolName, string EntityType, Guid? EntityId, string EntityTitle, string Status);
