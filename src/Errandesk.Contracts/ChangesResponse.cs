namespace Errandesk.Contracts;

/// <summary>
/// Lists, tasks and notes of the user that changed after a change version, including deletions.
/// </summary>
/// <param name="CurrentVersion">The global change version when the changes were read. Pass it as <c>since</c> in the next request.</param>
/// <param name="Lists">Lists that were created or changed.</param>
/// <param name="Tasks">Tasks that were created or changed.</param>
/// <param name="Notes">Notes that were created or changed.</param>
/// <param name="DeletedLists">Lists that were deleted, and lists the user can no longer see because the owner removed them or they left. The client drops both.</param>
/// <param name="DeletedTasks">Tasks that were deleted, including tasks deleted together with their list, and tasks the user can no longer see because they lost access to the list or the task moved to a list they cannot see.</param>
/// <param name="DeletedNotes">Notes that were deleted.</param>
/// <param name="ResetRequired">
/// <see langword="true"/> when <c>since</c> is older than the oldest retained deletion, so deletions may be missing.
/// The response then holds every current list, task and note, as for <c>since=0</c>, and the client replaces its synchronized data with it.
/// </param>
public sealed record ChangesResponse(long CurrentVersion, IReadOnlyList<TaskListResponse> Lists, IReadOnlyList<TaskItemResponse> Tasks, IReadOnlyList<NoteResponse> Notes, IReadOnlyList<TombstoneResponse> DeletedLists, IReadOnlyList<TombstoneResponse> DeletedTasks, IReadOnlyList<TombstoneResponse> DeletedNotes, bool ResetRequired = false);
