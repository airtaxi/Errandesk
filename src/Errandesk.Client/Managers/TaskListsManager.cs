using System.Collections.ObjectModel;
using Errandesk.Client.Models;
using Errandesk.Contracts;

namespace Errandesk.Client.Managers;

/// <summary>
/// Keeps the signed-in user's task lists, shared by the navigation pane and the task pages, in step with the local store.
/// </summary>
/// <remarks>
/// It follows <see cref="SyncManager.DataChanged"/> from its construction, before any page, so list names are current when pages apply the same changes.
/// </remarks>
public sealed class TaskListsManager
{
    private readonly SyncManager _syncManager;
    private readonly SessionManager _sessionManager;

    public TaskListsManager(SyncManager syncManager, SessionManager sessionManager)
    {
        _syncManager = syncManager;
        _sessionManager = sessionManager;
        _syncManager.DataChanged += OnSyncManagerDataChanged;
    }

    /// <summary>
    /// The lists in ascending sort order, the default list included.
    /// </summary>
    public ObservableCollection<TaskListModel> Lists { get; } = [];

    public TaskListModel? DefaultList => Lists.FirstOrDefault(list => list.IsDefault);

    /// <summary>
    /// Shows the lists stored on this device.
    /// </summary>
    public void Load()
    {
        Lists.Clear();
        foreach (var response in _syncManager.Data.GetLists().OrderBy(response => response.SortOrder)) Lists.Add(new TaskListModel(response, _sessionManager.CurrentUser?.Id));
    }

    public TaskListModel? Find(Guid listId) => Lists.FirstOrDefault(list => list.Id == listId);

    public string GetDisplayName(Guid listId) => Find(listId)?.DisplayName ?? string.Empty;

    public TaskListModel Create(string name)
    {
        var response = _syncManager.Data.CreateList(name);
        return Find(response.Id) ?? AddList(response);
    }

    public void Rename(TaskListModel list, string name) => list.Apply(_syncManager.Data.UpdateList(list.Id, new UpdateTaskListRequest { Name = name }));

    public void Delete(TaskListModel list)
    {
        _syncManager.Data.DeleteList(list.Id);
        Lists.Remove(list);
    }

    public void Clear() => Lists.Clear();

    /// <summary>
    /// Applies lists changed or deleted on this device or elsewhere. New lists are placed by their sort order.
    /// </summary>
    private void OnSyncManagerDataChanged(object? _, ChangesResponse changes)
    {
        if (changes.ResetRequired)
        {
            Load();
            return;
        }

        foreach (var tombstone in changes.DeletedLists)
        {
            if (Find(tombstone.Id) is { } deletedList)
            {
                Lists.Remove(deletedList);
            }
        }

        foreach (var response in changes.Lists)
        {
            if (Find(response.Id) is { } list) list.Apply(response);
            else AddList(response);
        }
    }

    private TaskListModel AddList(TaskListResponse response)
    {
        var list = new TaskListModel(response, _sessionManager.CurrentUser?.Id);
        var index = 0;
        while (index < Lists.Count && Lists[index].SortOrder <= response.SortOrder) index++;
        Lists.Insert(index, list);
        return list;
    }
}
