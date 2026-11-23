namespace Errandesk.Client.Sync;

/// <summary>
/// The number of open tasks in each smart list and in each list.
/// </summary>
/// <param name="MyDay">Open tasks added to My Day today.</param>
/// <param name="Important">Open tasks marked as important.</param>
/// <param name="Planned">Open tasks with a due date.</param>
/// <param name="ListCounts">Open tasks by list ID. Lists without open tasks are left out.</param>
public sealed record OpenTaskCounts(int MyDay, int Important, int Planned, IReadOnlyDictionary<Guid, int> ListCounts)
{
    public static OpenTaskCounts Empty { get; } = new(0, 0, 0, new Dictionary<Guid, int>());

    public int GetListCount(Guid listId) => ListCounts.GetValueOrDefault(listId);
}
