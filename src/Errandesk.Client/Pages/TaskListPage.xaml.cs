using System.ComponentModel;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Errandesk.Client.Views;
using Errandesk.Contracts;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Errandesk.Client.Pages;

/// <summary>
/// The tasks of a smart list or task list. On wide windows the selected task opens in a pane on the right; on narrow windows it replaces the list
/// and going back closes it.
/// </summary>
public sealed partial class TaskListPage : Page, IBackNavigationHandler
{
    private const double WideLayoutMinimumWidth = 640;
    private const double DetailPaneWidth = 360;
    private const string MyDayGlyph = "";
    private const string ImportantGlyph = "";
    private const string CompletedGlyph = "";
    private const string DueTodayGlyph = "";
    private const string DueTomorrowGlyph = "";
    private const string PickDateGlyph = "";
    private const string RemoveDueDateGlyph = "";
    private const string MoveGlyph = "";
    private const string DeleteGlyph = "";
    private const string DefaultListGlyph = "";
    private const string ListGlyph = "";
    private const string SharedListGlyph = "";
    private const string MyDayAcceleratorText = "Ctrl+T";
    private const string CompletionAcceleratorText = "Ctrl+D";
    private const string DeleteAcceleratorText = "Delete";

    private readonly SyncManager _syncManager = App.GetService<SyncManager>();
    private readonly MorningBriefingManager _briefingManager = App.GetService<MorningBriefingManager>();
    private readonly BackNavigationManager _backNavigationManager = App.GetService<BackNavigationManager>();
    private bool _isSelectionChanging;

    public TaskListPage() => InitializeComponent();

    public TaskListViewModel ViewModel { get; } = App.GetService<TaskListViewModel>();

    public bool CanGoBack => ViewModel.SelectedTask is not null && !IsWideLayout();

    /// <summary>
    /// Saves the title and note still being edited, then closes the details and shows the list again.
    /// </summary>
    public void GoBack()
    {
        TaskDetailView.CommitPendingEdits();
        ViewModel.Detail.CloseCommand.Execute(null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is TaskListPageParameter parameter) ViewModel.Load(parameter.View, parameter.SelectedTaskId);
    }

    /// <summary>
    /// Opens a task of the shown view in the detail pane, loading the view again when the task is not in it yet.
    /// </summary>
    public void SelectTask(Guid taskId) => ViewModel.SelectTask(taskId);

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _syncManager.DataChanged += OnSyncManagerDataChanged;
        _briefingManager.BriefingChanged += OnBriefingManagerBriefingChanged;
        ViewModel.UpdateBriefing();
        SyncListSelection();
        UpdatePaneLayout();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _syncManager.DataChanged -= OnSyncManagerDataChanged;
        _briefingManager.BriefingChanged -= OnBriefingManagerBriefingChanged;
    }

    private void OnBriefingManagerBriefingChanged(object? _, EventArgs __) => ViewModel.UpdateBriefing();

    private void OnSyncManagerDataChanged(object? _, ChangesResponse changes) => ViewModel.ApplyChanges(changes);

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePaneLayout();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskListViewModel.Rows)) RestoreListSelection();
        else if (e.PropertyName == nameof(TaskListViewModel.SelectedTask))
        {
            SyncListSelection();
            UpdatePaneLayout();
        }
    }

    /// <summary>
    /// Selects and scrolls to the row of the selected task, for example after a notification opened it, or clears the selection.
    /// </summary>
    private void SyncListSelection()
    {
        if (_isSelectionChanging) return;
        if (ViewModel.SelectedTask is not { } selectedTask || !ViewModel.Rows.Contains(selectedTask))
        {
            TasksListView.SelectedItem = null;
            return;
        }

        if (ReferenceEquals(TasksListView.SelectedItem, selectedTask)) return;
        TasksListView.SelectedItem = selectedTask;
        TasksListView.ScrollIntoView(selectedTask);
    }

    /// <summary>
    /// Selects the row of the selected task without scrolling, after the rows were replaced or a row that cannot be selected was chosen.
    /// </summary>
    private void RestoreListSelection() => TasksListView.SelectedItem = ViewModel.SelectedTask is { } selectedTask && ViewModel.Rows.Contains(selectedTask) ? selectedTask : null;

    /// <summary>
    /// Opens the only selected task in the detail pane and closes the pane while several tasks are selected. The completed section header cannot be selected.
    /// </summary>
    private void OnTasksListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectionChanging) return;

        if (TasksListView.SelectedItems.Contains(ViewModel.CompletedSection))
        {
            _isSelectionChanging = true;
            try { TasksListView.SelectedItems.Remove(ViewModel.CompletedSection); }
            finally { _isSelectionChanging = false; }

            if (TasksListView.SelectedItems.Count == 0) RestoreListSelection();
            return;
        }

        UpdateSelectedTaskFromList();
    }

    /// <summary>
    /// Shows the selected task in the detail pane when exactly one task is selected, and no task when several are. An empty selection leaves the pane as it is.
    /// </summary>
    private void UpdateSelectedTaskFromList()
    {
        var selectedTasks = GetSelectedTasks();
        if (selectedTasks.Count == 0) return;

        var task = selectedTasks.Count == 1 ? selectedTasks[0] : null;
        if (ReferenceEquals(ViewModel.SelectedTask, task)) return;

        TaskDetailView.CommitPendingEdits();
        _isSelectionChanging = true;
        try { ViewModel.SelectedTask = task; }
        finally { _isSelectionChanging = false; }
    }

    private List<TaskItemModel> GetSelectedTasks() =>
    [.. TasksListView.SelectedItems.OfType<TaskItemModel>()];

    /// <summary>
    /// Opens the task menu for the right-clicked task. A task outside the selection becomes the selection on wide layouts; on narrow layouts, where selecting opens the detail pane over the list, the menu acts on that task alone.
    /// </summary>
    private void OnTasksListViewContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((args.OriginalSource as FrameworkElement)?.DataContext is not TaskItemModel task) return;
        args.Handled = true;

        List<TaskItemModel> tasks;
        if (TasksListView.SelectedItems.Contains(task)) tasks = GetSelectedTasks();
        else
        {
            if (IsWideLayout()) TasksListView.SelectedItem = task;
            tasks = [task];
        }

        var menuFlyout = CreateTaskMenuFlyout(tasks);
        if (args.TryGetPosition(TasksListView, out var position)) menuFlyout.ShowAt(TasksListView, new FlyoutShowOptions { Position = position });
        else menuFlyout.ShowAt(TasksListView.ContainerFromItem(task) as FrameworkElement ?? TasksListView);
    }

    private MenuFlyout CreateTaskMenuFlyout(IReadOnlyList<TaskItemModel> tasks)
    {
        var isEveryTaskInMyDay = tasks.All(task => task.IsInMyDay);
        var isEveryTaskImportant = tasks.All(task => task.IsImportant);
        var isEveryTaskCompleted = tasks.All(task => task.IsCompleted);

        var menuFlyout = new MenuFlyout();
        menuFlyout.Items.Add(CreateMenuItem(isEveryTaskInMyDay ? "RemoveFromMyDayText" : "AddToMyDayText", MyDayGlyph, "TaskMyDayMenuItem", MyDayAcceleratorText, () => RunTaskAction(() => ViewModel.SetInMyDay(tasks, !isEveryTaskInMyDay))));
        menuFlyout.Items.Add(CreateMenuItem(isEveryTaskImportant ? "RemoveImportanceText" : "MarkImportantText", ImportantGlyph, "TaskImportanceMenuItem", null, () => RunTaskAction(() => ViewModel.SetImportant(tasks, !isEveryTaskImportant))));
        menuFlyout.Items.Add(CreateMenuItem(isEveryTaskCompleted ? "MarkNotCompletedText" : "MarkCompletedText", CompletedGlyph, "TaskCompletionMenuItem", CompletionAcceleratorText, () => RunTaskAction(() => ViewModel.SetCompleted(tasks, !isEveryTaskCompleted))));
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        menuFlyout.Items.Add(CreateMenuItem("TaskMenuDueTodayText", DueTodayGlyph, "TaskDueTodayMenuItem", null, () => RunTaskAction(() => ViewModel.SetDueDate(tasks, DateDisplayHelper.Today))));
        menuFlyout.Items.Add(CreateMenuItem("TaskMenuDueTomorrowText", DueTomorrowGlyph, "TaskDueTomorrowMenuItem", null, () => RunTaskAction(() => ViewModel.SetDueDate(tasks, DateDisplayHelper.Today.AddDays(1)))));
        menuFlyout.Items.Add(CreateMenuItem("TaskMenuPickDateText", PickDateGlyph, "TaskPickDateMenuItem", null, () => _ = PickDueDateAsync(tasks)));
        if (tasks.Any(task => task.DueDate is not null)) menuFlyout.Items.Add(CreateMenuItem("TaskMenuRemoveDueDateText", RemoveDueDateGlyph, "TaskRemoveDueDateMenuItem", null, () => RunTaskAction(() => ViewModel.SetDueDate(tasks, null))));
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        menuFlyout.Items.Add(CreateMoveMenuItem(tasks));
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        menuFlyout.Items.Add(CreateMenuItem("TaskMenuDeleteText", DeleteGlyph, "TaskDeleteMenuItem", DeleteAcceleratorText, () => _ = RunTaskActionAsync(() => ViewModel.DeleteTasksAsync(tasks))));
        return menuFlyout;
    }

    private MenuFlyoutSubItem CreateMoveMenuItem(IReadOnlyList<TaskItemModel> tasks)
    {
        var moveMenuItem = new MenuFlyoutSubItem { Text = LocalizedStrings.Get("TaskMenuMoveToText"), Icon = new FontIcon { Glyph = MoveGlyph } };
        AutomationProperties.SetAutomationId(moveMenuItem, "TaskMoveMenuItem");
        foreach (var list in ViewModel.GetMoveTargetLists())
        {
            var listId = list.Id;
            var listMenuItem = new MenuFlyoutItem { Text = list.DisplayName, Icon = new FontIcon { Glyph = list.IsDefault ? DefaultListGlyph : list.IsShared ? SharedListGlyph : ListGlyph }, IsEnabled = tasks.Any(task => task.ListId != listId) };
            listMenuItem.Click += (_, __) => RunTaskAction(() => ViewModel.MoveToList(tasks, listId));
            moveMenuItem.Items.Add(listMenuItem);
        }
        return moveMenuItem;
    }

    private static MenuFlyoutItem CreateMenuItem(string resourceName, string glyph, string automationId, string? acceleratorText, Action onClick)
    {
        var menuItem = new MenuFlyoutItem { Text = LocalizedStrings.Get(resourceName), Icon = new FontIcon { Glyph = glyph } };
        if (acceleratorText is not null) menuItem.KeyboardAcceleratorTextOverride = acceleratorText;
        AutomationProperties.SetAutomationId(menuItem, automationId);
        menuItem.Click += (_, __) => onClick();
        return menuItem;
    }

    private async Task PickDueDateAsync(IReadOnlyList<TaskItemModel> tasks)
    {
        var calendarView = new CalendarView { SelectionMode = CalendarViewSelectionMode.Single };
        AutomationProperties.SetAutomationId(calendarView, "DueDateCalendarView");
        if (!await App.GetService<DialogService>().ShowContentAsync(LocalizedStrings.Get("PickDueDateDialogTitle"), calendarView, LocalizedStrings.Get("DialogSaveButtonText"))) return;
        if (calendarView.SelectedDates.Count == 0) return;

        var dueDate = DateOnly.FromDateTime(calendarView.SelectedDates[0].DateTime);
        RunTaskAction(() => ViewModel.SetDueDate(tasks, dueDate));
    }

    /// <summary>
    /// Runs a change of several tasks without letting rows that move or leave during the change open the detail pane, then selects the tasks that are still shown.
    /// </summary>
    private void RunTaskAction(Action action)
    {
        TaskDetailView.CommitPendingEdits();
        var selectedTasks = GetSelectedTasks();
        _isSelectionChanging = true;
        try
        {
            action();
            RestoreSelectedTasks(selectedTasks);
        }
        finally { _isSelectionChanging = false; }

        UpdateSelectedTaskFromList();
    }

    private async Task RunTaskActionAsync(Func<Task> action)
    {
        TaskDetailView.CommitPendingEdits();
        var selectedTasks = GetSelectedTasks();
        _isSelectionChanging = true;
        try
        {
            await action();
            RestoreSelectedTasks(selectedTasks);
        }
        finally { _isSelectionChanging = false; }

        UpdateSelectedTaskFromList();
    }

    private void RestoreSelectedTasks(List<TaskItemModel> tasks)
    {
        TasksListView.SelectedItems.Clear();
        foreach (var task in tasks.Where(ViewModel.Rows.Contains)) TasksListView.SelectedItems.Add(task);
    }

    private void OnMyDayKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryGetKeyboardTargetTasks(args, out var tasks)) return;
        var isInMyDay = !tasks.All(task => task.IsInMyDay);
        RunTaskAction(() => ViewModel.SetInMyDay(tasks, isInMyDay));
    }

    private void OnCompletionKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!TryGetKeyboardTargetTasks(args, out var tasks)) return;
        var isCompleted = !tasks.All(task => task.IsCompleted);
        RunTaskAction(() => ViewModel.SetCompleted(tasks, isCompleted));
    }

    private void OnDeleteKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (TryGetKeyboardTargetTasks(args, out var tasks))
        {
            _ = RunTaskActionAsync(() => ViewModel.DeleteTasksAsync(tasks));
        }
    }

    /// <summary>
    /// The selected tasks for a task shortcut, which applies only while the focus is in the task list so that typing elsewhere keeps its keys.
    /// </summary>
    private bool TryGetKeyboardTargetTasks(KeyboardAcceleratorInvokedEventArgs args, out List<TaskItemModel> tasks)
    {
        tasks = IsFocusWithinTasksList() ? GetSelectedTasks() : [];
        if (tasks.Count == 0) return false;

        args.Handled = true;
        return true;
    }

    private bool IsFocusWithinTasksList()
    {
        for (var element = XamlRoot is { } xamlRoot ? FocusManager.GetFocusedElement(xamlRoot) as DependencyObject : null; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, TasksListView))
            {
                return true;
            }
        }
        return false;
    }

    private void OnTasksListViewItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CompletedSectionModel)
        {
            ViewModel.ToggleCompletedSectionCommand.Execute(null);
        }
    }

    private void OnTaskCompletionCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TaskItemModel task } checkBox) return;
        ViewModel.ToggleCompletion(task);
        if (ReferenceEquals(checkBox.DataContext, task)) checkBox.IsChecked = task.IsCompleted;
    }

    private void OnTaskImportanceButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItemModel task })
        {
            ViewModel.ToggleImportance(task);
        }
    }

    /// <summary>
    /// Opens the share dialog of the shown list. A member who chooses to leave is asked to confirm after the dialog closed.
    /// </summary>
    private async void OnShareListButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentList is not { } list) return;

        var shareListViewModel = App.GetService<ShareListViewModel>();
        shareListViewModel.Initialize(list);
        bool isLeaveRequested;
        try { isLeaveRequested = await App.GetService<DialogService>().ShowPanelAsync(shareListViewModel.DialogTitle, new ShareListView(shareListViewModel), LocalizedStrings.Get("DialogCloseButtonText"), shareListViewModel.IsOwner ? null : LocalizedStrings.Get("LeaveListButtonText")); }
        finally { shareListViewModel.Close(); }

        if (isLeaveRequested) await ViewModel.LeaveListAsync(shareListViewModel);
    }

    private void OnNewTaskTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.AddTaskCommand.CanExecute(null)) return;
        e.Handled = true;
        ViewModel.NewTaskTitle = ((TextBox)sender).Text;
        ViewModel.AddTaskCommand.Execute(null);
    }

    private void OnNewTaskKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.SelectedTask is not null && !IsWideLayout()) ViewModel.SelectedTask = null;
        NewTaskTextBox.Focus(FocusState.Keyboard);
    }

    /// <summary>
    /// Whether the page itself is wide enough for the list and the detail pane side by side. The page narrows when the assistant pane is docked next to it.
    /// </summary>
    private bool IsWideLayout() => ActualWidth >= WideLayoutMinimumWidth;

    private void UpdatePaneLayout()
    {
        var isWideLayout = IsWideLayout();
        var isDetailOpen = ViewModel.SelectedTask is not null;
        var isListVisible = isWideLayout || !isDetailOpen;

        ListPanel.Visibility = isListVisible ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = isListVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DetailPanel.Visibility = isDetailOpen ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.BorderThickness = isWideLayout ? new Thickness(1, 0, 0, 0) : new Thickness(0);
        DetailColumn.Width = !isDetailOpen ? new GridLength(0) : isWideLayout ? new GridLength(DetailPaneWidth) : new GridLength(1, GridUnitType.Star);
        TaskDetailView.IsBackButtonVisible = !isWideLayout;
        _backNavigationManager.Update();
    }
}
