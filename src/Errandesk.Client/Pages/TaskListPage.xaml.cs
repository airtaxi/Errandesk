using System.ComponentModel;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Errandesk.Client.Views;
using Errandesk.Contracts;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Errandesk.Client.Pages;

/// <summary>
/// The tasks of a smart list or task list. On wide windows the selected task opens in a pane on the right; on narrow windows it replaces the list.
/// </summary>
public sealed partial class TaskListPage : Page
{
    private const double WideLayoutMinimumWidth = 640;
    private const double DetailPaneWidth = 360;

    private readonly SyncManager _syncManager = App.GetService<SyncManager>();
    private readonly MorningBriefingManager _briefingManager = App.GetService<MorningBriefingManager>();

    public TaskListPage() => InitializeComponent();

    public TaskListViewModel ViewModel { get; } = App.GetService<TaskListViewModel>();

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

    private void OnTasksListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TasksListView.SelectedItem is CompletedSectionModel)
        {
            RestoreListSelection();
            return;
        }

        if (TasksListView.SelectedItem is not TaskItemModel task || ReferenceEquals(ViewModel.SelectedTask, task)) return;
        TaskDetailView.CommitPendingEdits();
        ViewModel.SelectedTask = task;
    }

    private void OnCompletedSectionButtonClicked(object sender, RoutedEventArgs e) => ViewModel.ToggleCompletedSectionCommand.Execute(null);

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
    }
}
