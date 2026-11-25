using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The tasks of one view: a smart list or a task list, with open and completed sections and the selected task's detail pane.
/// </summary>
public sealed partial class TaskListViewModel : ObservableObject
{
    private readonly SyncManager _syncManager;
    private readonly TaskListsManager _taskListsManager;
    private readonly DialogService _dialogService;
    private readonly MorningBriefingManager _briefingManager;
    private readonly AssistantNavigationManager _assistantNavigationManager;
    private readonly HashSet<Guid> _locallyChangedTaskIds = [];
    private bool _isLoadingTasks;

    public TaskListViewModel(SyncManager syncManager, TaskListsManager taskListsManager, DialogService dialogService, MorningBriefingManager briefingManager, AssistantNavigationManager assistantNavigationManager, TaskDetailViewModel detail)
    {
        _syncManager = syncManager;
        _taskListsManager = taskListsManager;
        _dialogService = dialogService;
        _briefingManager = briefingManager;
        _assistantNavigationManager = assistantNavigationManager;
        Detail = detail;
        Detail.TaskChanged += OnDetailTaskChanged;
        Detail.TaskDeleted += OnDetailTaskDeleted;
        Detail.CloseRequested += OnDetailCloseRequested;
        OpenTasks.CollectionChanged += OnOpenTasksCollectionChanged;
        CompletedTasks.CollectionChanged += OnCompletedTasksCollectionChanged;
    }

    public TaskDetailViewModel Detail { get; }

    public TaskViewDescriptor View { get; private set; } = TaskViewDescriptor.MyDay;

    public ObservableCollection<TaskItemModel> OpenTasks { get; } = [];

    public ObservableCollection<TaskItemModel> CompletedTasks { get; } = [];

    public CompletedSectionModel CompletedSection { get; } = new();

    /// <summary>
    /// The rows of the task list: the open tasks, then the completed section header and, when the section is expanded, the completed tasks.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<object> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeaderDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsHeaderDateVisible { get; set; }

    /// <summary>
    /// Whether the view is a list the user owns other than the default list, which can be renamed and deleted.
    /// </summary>
    [ObservableProperty]
    public partial bool IsListActionsVisible { get; set; }

    /// <summary>
    /// Whether the view is a list the user owns other than the default list, which the user can share.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShareVisible { get; set; }

    /// <summary>
    /// Whether the view is a list someone else shared with the user, whose people the user can see and which the user can leave.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMembersVisible { get; set; }

    /// <summary>
    /// Who else can see the list, such as "Shared by Alice" or "Shared with 3 people"; empty when the list is not shared.
    /// </summary>
    [ObservableProperty]
    public partial string SharingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSharingTextVisible { get; set; }

    /// <summary>
    /// The list shown, or <see langword="null"/> in smart views.
    /// </summary>
    public TaskListModel? CurrentList => View.ListId is { } listId ? _taskListsManager.Find(listId) : null;

    [ObservableProperty]
    public partial string NewTaskTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmptyStateVisible { get; set; }

    [ObservableProperty]
    public partial bool IsCompletedExpanded { get; set; } = true;

    [ObservableProperty]
    public partial TaskItemModel? SelectedTask { get; set; }

    /// <summary>
    /// Whether today's morning briefing is shown at the top of My Day.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBriefingVisible { get; set; }

    [ObservableProperty]
    public partial string BriefingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    partial void OnSelectedTaskChanged(TaskItemModel? value) => Detail.CurrentTask = value;

    partial void OnIsCompletedExpandedChanged(bool value)
    {
        CompletedSection.IsExpanded = value;
        RebuildRows();
    }

    /// <summary>
    /// Shows the tasks of <paramref name="view"/> stored on this device and opens <paramref name="taskIdToSelect"/> in the detail pane when it is among them.
    /// </summary>
    public void Load(TaskViewDescriptor view, Guid? taskIdToSelect = null)
    {
        LoadTasks(view);
        if (taskIdToSelect is { } taskId) TrySelectTask(taskId);
        UpdateBriefing();
        if (View.Kind == TaskViewKind.MyDay && _syncManager.IsOnline) _ = _briefingManager.RefreshAsync(isForced: true);
    }

    /// <summary>
    /// Shows today's morning briefing in My Day, or hides it in other views and when there is none.
    /// </summary>
    public void UpdateBriefing()
    {
        var briefing = View.Kind == TaskViewKind.MyDay ? _briefingManager.Briefing : null;
        BriefingText = briefing?.Text ?? string.Empty;
        IsBriefingVisible = briefing is not null;
    }

    [RelayCommand]
    private void DismissBriefing() => _briefingManager.Dismiss();

    /// <summary>
    /// Opens the briefing conversation in the assistant pane, where the user can ask about the briefing.
    /// </summary>
    [RelayCommand]
    private void OpenBriefingInAssistant()
    {
        if (_briefingManager.Briefing is { } briefing)
        {
            _assistantNavigationManager.OpenConversation(briefing.ConversationId);
        }
    }

    /// <summary>
    /// Opens a task in the detail pane, loading the view again when the task is not shown yet.
    /// </summary>
    public void SelectTask(Guid taskId)
    {
        if (!TrySelectTask(taskId))
        {
            Load(View, taskId);
        }
    }

    private bool TrySelectTask(Guid taskId)
    {
        if (FindTask(taskId) is not { } task) return false;

        if (task.IsCompleted) IsCompletedExpanded = true;
        SelectedTask = task;
        return true;
    }

    private void LoadTasks(TaskViewDescriptor view)
    {
        View = view;
        SelectedTask = null;
        _isLoadingTasks = true;
        OpenTasks.Clear();
        CompletedTasks.Clear();
        _locallyChangedTaskIds.Clear();
        UpdateHeader();

        IsErrorVisible = false;
        try
        {
            foreach (var response in _syncManager.Data.GetTasks(view.QueryValue, includeCompleted: true))
            {
                var task = CreateTaskModel(response);
                if (task.IsCompleted) CompletedTasks.Add(task);
                else OpenTasks.Add(task);
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            _isLoadingTasks = false;
            UpdateCompletedSection();
            RebuildRows();
            UpdateEmptyState();
        }
    }

    /// <summary>
    /// Replaces the rows at once, so a list with many tasks is handed to the list view in one change instead of one change per task.
    /// </summary>
    private void RebuildRows()
    {
        var rows = new List<object>(OpenTasks.Count + CompletedTasks.Count + 1);
        rows.AddRange(OpenTasks);
        if (CompletedTasks.Count > 0)
        {
            rows.Add(CompletedSection);
            if (IsCompletedExpanded) rows.AddRange(CompletedTasks);
        }

        Rows = new ObservableCollection<object>(rows);
    }

    private void OnOpenTasksCollectionChanged(object? _, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoadingTasks) return;
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is [var addedTask]) Rows.Insert(e.NewStartingIndex, addedTask!);
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is { Count: 1 }) Rows.RemoveAt(e.OldStartingIndex);
        else RebuildRows();
    }

    /// <summary>
    /// Mirrors a change of the completed tasks in the rows, which hold the section header after the open tasks while any task is completed.
    /// </summary>
    private void OnCompletedTasksCollectionChanged(object? _, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoadingTasks) return;
        UpdateCompletedSection();
        var sectionIndex = OpenTasks.Count;
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is [var addedTask])
        {
            if (CompletedTasks.Count == 1) Rows.Insert(sectionIndex, CompletedSection);
            if (IsCompletedExpanded) Rows.Insert(sectionIndex + 1 + e.NewStartingIndex, addedTask!);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is { Count: 1 })
        {
            if (IsCompletedExpanded) Rows.RemoveAt(sectionIndex + 1 + e.OldStartingIndex);
            if (CompletedTasks.Count == 0) Rows.RemoveAt(sectionIndex);
        }
        else RebuildRows();
    }

    /// <summary>
    /// Applies tasks and lists changed on this device or elsewhere. New tasks that belong to the view are added, deleted tasks are removed,
    /// and tasks that another device or the assistant moved out of the view leave it unless they are open in the detail pane or were changed in this view.
    /// A replaced data set loads the view again.
    /// </summary>
    public void ApplyChanges(ChangesResponse changes)
    {
        if (changes.ResetRequired)
        {
            Load(View, SelectedTask?.Id);
            return;
        }

        foreach (var tombstone in changes.DeletedTasks)
        {
            if (FindTask(tombstone.Id) is { } deletedTask)
            {
                RemoveTask(deletedTask);
            }
        }

        foreach (var response in changes.Tasks)
        {
            if (FindTask(response.Id) is not { } task)
            {
                if (BelongsToView(response)) AddTask(CreateTaskModel(response));
                continue;
            }

            var isNewerThanShown = response.ChangeVersion > task.ChangeVersion;
            if (!task.Apply(response)) continue;
            if (isNewerThanShown && !BelongsToView(response) && !ReferenceEquals(task, SelectedTask) && !_locallyChangedTaskIds.Contains(task.Id)) RemoveTask(task);
            else PlaceTask(task);
            if (ReferenceEquals(task, SelectedTask)) Detail.RefreshFromCurrentTask();
        }

        if (changes.Lists.Count > 0 || changes.DeletedLists.Count > 0)
        {
            foreach (var task in OpenTasks.Concat(CompletedTasks)) task.ListName = _taskListsManager.GetDisplayName(task.ListId);
            if (View.ListId is null || _taskListsManager.Find(View.ListId.Value) is not null) UpdateHeader();
        }

        UpdateEmptyState();
    }

    public void ToggleCompletion(TaskItemModel task) => UpdateTasks((TaskItemModel[])[task], new UpdateTaskRequest { IsCompleted = !task.IsCompleted });

    public void ToggleImportance(TaskItemModel task) => UpdateTasks((TaskItemModel[])[task], new UpdateTaskRequest { IsImportant = !task.IsImportant });

    /// <summary>
    /// The lists tasks can be moved to, in the order of the navigation pane.
    /// </summary>
    public List<TaskListModel> GetMoveTargetLists() =>
    [.. _taskListsManager.Lists];

    /// <summary>
    /// Adds the tasks to My Day or removes them. Tasks removed from My Day leave the My Day view.
    /// </summary>
    public void SetInMyDay(IReadOnlyList<TaskItemModel> tasks, bool isInMyDay) => UpdateTasks(tasks, new UpdateTaskRequest { IsInMyDay = isInMyDay }, removesTasksOutsideView: !isInMyDay);

    public void SetImportant(IReadOnlyList<TaskItemModel> tasks, bool isImportant) => UpdateTasks(tasks, new UpdateTaskRequest { IsImportant = isImportant });

    public void SetCompleted(IReadOnlyList<TaskItemModel> tasks, bool isCompleted) => UpdateTasks(tasks, new UpdateTaskRequest { IsCompleted = isCompleted });

    /// <summary>
    /// Sets the due date of the tasks, or removes it when <paramref name="dueDate"/> is <see langword="null"/>.
    /// </summary>
    public void SetDueDate(IReadOnlyList<TaskItemModel> tasks, DateOnly? dueDate) => UpdateTasks(tasks, new UpdateTaskRequest { DueDate = dueDate });

    /// <summary>
    /// Moves the tasks to another list. Tasks that no longer belong to the view leave it.
    /// </summary>
    public void MoveToList(IReadOnlyList<TaskItemModel> tasks, Guid listId) => UpdateTasks(tasks.Where(task => task.ListId != listId).ToList(), new UpdateTaskRequest { ListId = listId }, removesTasksOutsideView: true);

    /// <summary>
    /// Asks for confirmation, then deletes the tasks.
    /// </summary>
    public async Task DeleteTasksAsync(IReadOnlyList<TaskItemModel> tasks)
    {
        if (tasks.Count == 0) return;

        var title = LocalizedStrings.Get(tasks.Count == 1 ? "DeleteTaskDialogTitle" : "DeleteTasksDialogTitle");
        var message = tasks.Count == 1 ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteTaskDialogMessage"), tasks[0].Title) : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteTasksDialogMessageFormat"), tasks.Count);
        if (!await _dialogService.ConfirmAsync(title, message, LocalizedStrings.Get("DeleteButtonText"))) return;

        IsErrorVisible = false;
        foreach (var task in tasks)
        {
            try
            {
                _syncManager.Data.DeleteTask(task.Id);
                RemoveTask(task);
            }
            catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        }

        UpdateEmptyState();
    }

    [RelayCommand]
    private void AddTask()
    {
        var title = NewTaskTitle.Trim();
        if (title.Length == 0) return;

        NewTaskTitle = string.Empty;
        var request = new CreateTaskRequest(title, Guid.CreateVersion7(), ListId: View.ListId, IsImportant: View.Kind == TaskViewKind.Important, DueDate: View.Kind == TaskViewKind.Planned ? DateDisplayHelper.Today : null, IsInMyDay: View.Kind == TaskViewKind.MyDay);
        IsErrorVisible = false;
        try
        {
            var response = _syncManager.Data.CreateTask(request);
            if (OpenTasks.Any(task => task.Id == response.Id)) return;

            var task = CreateTaskModel(response);
            if (View.Kind == TaskViewKind.Planned) InsertOpenTask(task);
            else OpenTasks.Insert(0, task);
            UpdateEmptyState();
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            NewTaskTitle = title;
            ShowError(exception);
        }
    }

    /// <summary>
    /// Asks for confirmation, then leaves the shared list through <paramref name="shareListViewModel"/>. The list and its tasks disappear from this device.
    /// </summary>
    public async Task LeaveListAsync(ShareListViewModel shareListViewModel)
    {
        if (CurrentList is not { IsOwnedByCurrentUser: false } list) return;

        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("LeaveListDialogMessageFormat"), list.DisplayName);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("LeaveListDialogTitle"), message, LocalizedStrings.Get("LeaveListButtonText"))) return;

        IsErrorVisible = false;
        try { await shareListViewModel.LeaveAsync(); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    [RelayCommand]
    private async Task RenameListAsync()
    {
        if (View.ListId is not { } listId || _taskListsManager.Find(listId) is not { IsDefault: false, IsOwnedByCurrentUser: true } list) return;

        var name = await _dialogService.RequestTextAsync(LocalizedStrings.Get("RenameListDialogTitle"), list.Name, LocalizedStrings.Get("ListNamePlaceholder"), LocalizedStrings.Get("RenameButtonText"));
        if (name is null || name == list.Name) return;

        IsErrorVisible = false;
        try
        {
            _taskListsManager.Rename(list, name);
            Title = list.DisplayName;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    [RelayCommand]
    private async Task DeleteListAsync()
    {
        if (View.ListId is not { } listId || _taskListsManager.Find(listId) is not { IsDefault: false, IsOwnedByCurrentUser: true } list) return;

        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteListDialogMessage"), list.Name);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("DeleteListDialogTitle"), message, LocalizedStrings.Get("DeleteButtonText"))) return;

        IsErrorVisible = false;
        try { _taskListsManager.Delete(list); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    [RelayCommand]
    private void ToggleCompletedSection() => IsCompletedExpanded = !IsCompletedExpanded;

    [RelayCommand]
    private void Refresh() => Load(View);

    /// <summary>
    /// Applies the same change to each task. With <paramref name="removesTasksOutsideView"/>, tasks that no longer belong to the view leave it right away.
    /// </summary>
    private void UpdateTasks(IReadOnlyList<TaskItemModel> tasks, UpdateTaskRequest request, bool removesTasksOutsideView = false)
    {
        IsErrorVisible = false;
        foreach (var task in tasks)
        {
            _locallyChangedTaskIds.Add(task.Id);
            try
            {
                var response = _syncManager.Data.UpdateTask(task.Id, request);
                if (task.Apply(response)) PlaceTask(task);
                if (removesTasksOutsideView && !BelongsToView(response)) RemoveTask(task);
            }
            catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        }

        UpdateEmptyState();
    }

    private TaskItemModel CreateTaskModel(TaskItemResponse response) => new(response, View.Kind)
    {
        ListName = _taskListsManager.GetDisplayName(response.ListId)
    };

    /// <summary>
    /// Moves a task between the open and completed sections after its completion changed. Tasks stay in the view until it reloads, even when they no longer match it.
    /// </summary>
    private void PlaceTask(TaskItemModel task)
    {
        task.ListName = _taskListsManager.GetDisplayName(task.ListId);
        if (task.IsCompleted && OpenTasks.Remove(task)) CompletedTasks.Insert(0, task);
        else if (!task.IsCompleted && CompletedTasks.Remove(task)) InsertOpenTask(task);
        UpdateEmptyState();
    }

    private TaskItemModel? FindTask(Guid taskId) => OpenTasks.FirstOrDefault(task => task.Id == taskId) ?? CompletedTasks.FirstOrDefault(task => task.Id == taskId);

    private bool BelongsToView(TaskItemResponse response) => View.Kind switch
    {
        TaskViewKind.MyDay => response.IsInMyDay,
        TaskViewKind.Important => response.IsImportant,
        TaskViewKind.Planned => response.DueDate is not null,
        _ => response.ListId == View.ListId,
    };

    private void AddTask(TaskItemModel task)
    {
        if (task.IsCompleted) CompletedTasks.Insert(0, task);
        else InsertOpenTask(task);
    }

    private void RemoveTask(TaskItemModel task)
    {
        if (ReferenceEquals(task, SelectedTask)) SelectedTask = null;
        OpenTasks.Remove(task);
        CompletedTasks.Remove(task);
    }

    private void InsertOpenTask(TaskItemModel task)
    {
        var index = 0;
        while (index < OpenTasks.Count && CompareOpenTasks(OpenTasks[index], task) <= 0) index++;
        OpenTasks.Insert(index, task);
    }

    private int CompareOpenTasks(TaskItemModel first, TaskItemModel second)
    {
        if (View.Kind == TaskViewKind.Planned)
        {
            var dueDateComparison = Nullable.Compare(first.DueDate, second.DueDate);
            if (dueDateComparison != 0) return dueDateComparison;
        }

        var sortOrderComparison = first.SortOrder.CompareTo(second.SortOrder);
        return sortOrderComparison != 0 ? sortOrderComparison : first.CreatedAt.CompareTo(second.CreatedAt);
    }

    private void UpdateHeader()
    {
        var list = View.ListId is { } listId ? _taskListsManager.Find(listId) : null;
        Title = View.Kind switch
        {
            TaskViewKind.MyDay => LocalizedStrings.Get("MyDayTitle"),
            TaskViewKind.Important => LocalizedStrings.Get("ImportantTitle"),
            TaskViewKind.Planned => LocalizedStrings.Get("PlannedTitle"),
            _ => list?.DisplayName ?? string.Empty,
        };
        IsHeaderDateVisible = View.Kind == TaskViewKind.MyDay;
        HeaderDateText = IsHeaderDateVisible ? DateDisplayHelper.FormatMyDayDate(DateDisplayHelper.Today) : string.Empty;
        IsListActionsVisible = list is { IsDefault: false, IsOwnedByCurrentUser: true };
        IsShareVisible = list is { IsDefault: false, IsOwnedByCurrentUser: true };
        IsMembersVisible = list is { IsDefault: false, IsOwnedByCurrentUser: false };
        SharingText = GetSharingText(list);
        IsSharingTextVisible = SharingText.Length > 0;
    }

    private static string GetSharingText(TaskListModel? list) => list switch
    {
        null or { IsShared: false } => string.Empty,
        { IsOwnedByCurrentUser: false } => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("SharedByFormat"), list.Owner?.DisplayName),
        { Members.Count: 1 } => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("SharedWithOneFormat"), list.Members[0].DisplayName),
        _ => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("SharedWithManyFormat"), list.Members.Count),
    };

    private void UpdateCompletedSection() => CompletedSection.HeaderText = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("CompletedSectionFormat"), CompletedTasks.Count);

    private void UpdateEmptyState() => IsEmptyStateVisible = !IsErrorVisible && OpenTasks.Count == 0 && CompletedTasks.Count == 0;

    private void OnDetailTaskChanged(object? _, TaskItemModel task)
    {
        _locallyChangedTaskIds.Add(task.Id);
        PlaceTask(task);
    }

    private void OnDetailTaskDeleted(object? _, TaskItemModel task)
    {
        SelectedTask = null;
        OpenTasks.Remove(task);
        CompletedTasks.Remove(task);
        UpdateEmptyState();
    }

    private void OnDetailCloseRequested(object? _, EventArgs __) => SelectedTask = null;

    private void ShowError(Exception exception)
    {
        if (ApiErrorMessages.GetMessage(exception) is not { } message) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
