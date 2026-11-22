namespace Errandesk.Contracts;

/// <summary>
/// Adds a step to a task.
/// </summary>
/// <param name="Title">Step title.</param>
/// <param name="Id">Client-generated ID. Adding a step whose ID already exists in the task changes nothing.</param>
/// <param name="IsCompleted">Whether the step starts completed.</param>
/// <param name="SortOrder">Position among the steps. Defaults to after the last step.</param>
public sealed record CreateTaskStepRequest(string Title, Guid? Id = null, bool IsCompleted = false, double? SortOrder = null);
