namespace Errandesk.Contracts;

/// <summary>
/// A step inside a task.
/// </summary>
public sealed record TaskStepResponse(Guid Id, string Title, bool IsCompleted, double SortOrder);
