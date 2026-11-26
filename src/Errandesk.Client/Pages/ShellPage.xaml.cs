using System.Collections.Specialized;
using System.ComponentModel;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;

namespace Errandesk.Client.Pages;

/// <summary>
/// The signed-in layout: the navigation pane with smart lists, the user's lists and settings, and the content frame.
/// </summary>
public sealed partial class ShellPage : Page, IBackNavigationHandler
{
    private const double MinimalModeContentTopOffset = 48;
    private const double AssistantPaneWidth = 400;
    private const double DockedPageMinimumWidth = 360;
    private const string ListGlyph = "\uE8FD";
    private const string SharedListGlyph = "\uE716";

    private readonly Dictionary<Guid, NavigationViewItem> _listNavigationItems = [];
    private readonly NoteNavigationManager _noteNavigationManager = App.GetService<NoteNavigationManager>();
    private readonly SyncManager _syncManager = App.GetService<SyncManager>();
    private readonly ReminderManager _reminderManager = App.GetService<ReminderManager>();
    private readonly TaskNavigationManager _taskNavigationManager = App.GetService<TaskNavigationManager>();
    private readonly AssistantNavigationManager _assistantNavigationManager = App.GetService<AssistantNavigationManager>();
    private readonly BackNavigationManager _backNavigationManager = App.GetService<BackNavigationManager>();
    private Guid? _pendingNoteId;
    private Guid? _pendingSelectedTaskId;
    private bool _isAssistantOpen;
    private bool _areListsLoaded;

    public ShellPage()
    {
        InitializeComponent();
        MyDayNavigationItem.Tag = TaskViewDescriptor.MyDay;
        ImportantNavigationItem.Tag = TaskViewDescriptor.Important;
        PlannedNavigationItem.Tag = TaskViewDescriptor.Planned;
    }

    public ShellViewModel ViewModel { get; } = App.GetService<ShellViewModel>();

    public bool CanGoBack => IsPaneOverContent() || IsAssistantReplacingContent() || ContentFrame.Content is IBackNavigationHandler { CanGoBack: true };

    /// <summary>
    /// Closes the navigation pane where it covers the content, then the assistant where it replaces the page, and otherwise goes back on the shown page.
    /// </summary>
    public void GoBack()
    {
        if (IsPaneOverContent()) ShellNavigationView.IsPaneOpen = false;
        else if (IsAssistantReplacingContent()) _ = SetAssistantOpenAsync(false);
        else if (ContentFrame.Content is IBackNavigationHandler { CanGoBack: true } handler) handler.GoBack();
    }

    private bool IsPaneOverContent() => ShellNavigationView.IsPaneOpen && ShellNavigationView.DisplayMode != NavigationViewDisplayMode.Expanded;

    private bool IsAssistantReplacingContent() => _isAssistantOpen && !IsAssistantDocked();

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e) => _backNavigationManager.Update();

    private void OnShellNavigationViewPaneOpened(NavigationView sender, object args) => _backNavigationManager.Update();

    private void OnShellNavigationViewPaneClosed(NavigationView sender, object args) => _backNavigationManager.Update();

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (ShellNavigationView.SettingsItem is NavigationViewItem settingsItem) AutomationProperties.SetAutomationId(settingsItem, "SettingsNavigationItem");
        ViewModel.Lists.CollectionChanged += OnListsCollectionChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _noteNavigationManager.NoteOpenRequested += OnNoteNavigationManagerNoteOpenRequested;
        _taskNavigationManager.TaskOpenRequested += OnTaskNavigationManagerTaskOpenRequested;
        _reminderManager.InAppReminderRaised += OnReminderManagerInAppReminderRaised;
        _assistantNavigationManager.ConversationOpenRequested += OnAssistantNavigationManagerConversationOpenRequested;
        _assistantNavigationManager.AssistantOpenRequested += OnAssistantNavigationManagerAssistantOpenRequested;
        _syncManager.Start();
        _ = _reminderManager.StartAsync();
        await LoadAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _reminderManager.Stop();
        _reminderManager.InAppReminderRaised -= OnReminderManagerInAppReminderRaised;
        _taskNavigationManager.TaskOpenRequested -= OnTaskNavigationManagerTaskOpenRequested;
        _assistantNavigationManager.ConversationOpenRequested -= OnAssistantNavigationManagerConversationOpenRequested;
        _assistantNavigationManager.AssistantOpenRequested -= OnAssistantNavigationManagerAssistantOpenRequested;
        _syncManager.Stop();
        AssistantView.ViewModel.StopCommand.Execute(null);
        ViewModel.Lists.CollectionChanged -= OnListsCollectionChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _noteNavigationManager.NoteOpenRequested -= OnNoteNavigationManagerNoteOpenRequested;
        foreach (var list in ViewModel.Lists) list.PropertyChanged -= OnListPropertyChanged;
        ViewModel.Unload();
    }

    private async Task LoadAsync()
    {
        if (!await ViewModel.LoadAsync()) return;
        RebuildListNavigationItems();
        _areListsLoaded = true;
        if (_taskNavigationManager.TryTakePendingRequest(out var request))
        {
            await OpenTaskAsync(request);
            return;
        }

        if (ShellNavigationView.SelectedItem is null) ShellNavigationView.SelectedItem = MyDayNavigationItem;
        if (_assistantNavigationManager.TryTakePendingAssistantRequest()) await SetAssistantOpenAsync(true);
    }

    private async void OnTaskNavigationManagerTaskOpenRequested(object? _, EventArgs __)
    {
        if (_areListsLoaded && _taskNavigationManager.TryTakePendingRequest(out var request))
        {
            await OpenTaskAsync(request);
        }
    }

    private void OnReminderManagerInAppReminderRaised(object? _, ReminderNotification reminder) => ViewModel.ShowInAppReminder(reminder);

    private void OnInAppReminderInfoBarCloseButtonClicked(InfoBar sender, object args) => ViewModel.HideInAppReminder();

    private async void OnOpenReminderTaskButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.InAppReminder is not { } reminder) return;
        ViewModel.HideInAppReminder();
        await OpenTaskAsync(new TaskNavigationRequest(reminder.TaskId, reminder.ListId));
    }

    /// <summary>
    /// Shows the list that contains the task and opens the task in the detail pane.
    /// </summary>
    private async Task OpenTaskAsync(TaskNavigationRequest request)
    {
        if (IsAssistantReplacingContent()) await SetAssistantOpenAsync(false);

        var listId = ViewModel.ResolveTaskListId(request);
        NavigationViewItem? navigationItem = listId is not { } resolvedListId ? null : ViewModel.DefaultList?.Id == resolvedListId ? TasksNavigationItem : _listNavigationItems.GetValueOrDefault(resolvedListId);
        if (navigationItem is null)
        {
            if (ShellNavigationView.SelectedItem is null) ShellNavigationView.SelectedItem = MyDayNavigationItem;
            return;
        }

        if (ReferenceEquals(ShellNavigationView.SelectedItem, navigationItem) && ContentFrame.Content is TaskListPage taskListPage)
        {
            taskListPage.SelectTask(request.TaskId);
            return;
        }

        _pendingSelectedTaskId = request.TaskId;
        ShellNavigationView.SelectedItem = navigationItem;
    }

    /// <summary>
    /// Moves the content below the pane toggle button, which floats over the content in minimal mode.
    /// </summary>
    private void OnShellNavigationViewDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        ContentGrid.Margin = args.DisplayMode == NavigationViewDisplayMode.Minimal ? new Thickness(0, MinimalModeContentTopOffset, 0, 0) : new Thickness(0);
        ApplyOpenTaskCounts(sender.IsPaneOpen);
        _backNavigationManager.Update();
    }

    private void OnShellNavigationViewPaneOpening(NavigationView sender, object args) => ApplyOpenTaskCounts(true);

    private void OnShellNavigationViewPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => ApplyOpenTaskCounts(false);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.OpenTaskCounts))
        {
            ApplyOpenTaskCounts(ShellNavigationView.IsPaneOpen);
        }
    }

    /// <summary>
    /// Shows the number of open tasks next to each smart list and list while the pane shows item names.
    /// </summary>
    /// <param name="isPaneOpen">Whether the pane shows item names; with only icons the counts are hidden.</param>
    private void ApplyOpenTaskCounts(bool isPaneOpen)
    {
        var counts = ViewModel.OpenTaskCounts;
        ApplyOpenTaskCount(MyDayNavigationItem, counts.MyDay, isPaneOpen);
        ApplyOpenTaskCount(ImportantNavigationItem, counts.Important, isPaneOpen);
        ApplyOpenTaskCount(PlannedNavigationItem, counts.Planned, isPaneOpen);
        ApplyOpenTaskCount(TasksNavigationItem, ViewModel.DefaultList is { } defaultList ? counts.GetListCount(defaultList.Id) : 0, isPaneOpen);
        foreach (var (listId, navigationItem) in _listNavigationItems) ApplyOpenTaskCount(navigationItem, counts.GetListCount(listId), isPaneOpen);
    }

    private void ApplyOpenTaskCount(NavigationViewItem navigationItem, int openTaskCount, bool isPaneOpen)
    {
        if (openTaskCount <= 0 || !isPaneOpen)
        {
            navigationItem.InfoBadge = null;
            return;
        }

        if (navigationItem.InfoBadge is not { } infoBadge) navigationItem.InfoBadge = infoBadge = new InfoBadge { Style = (Style)Resources["OpenTaskCountInfoBadgeStyle"] };
        infoBadge.Value = openTaskCount;
    }

    private async void OnShellRetryButtonClicked(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnShellNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (IsAssistantReplacingContent()) await SetAssistantOpenAsync(false);
        if (_areListsLoaded) ViewModel.RefreshOpenTaskCounts();
        if (args.IsSettingsSelected) ContentFrame.Navigate(typeof(SettingsPage));
        else if (ReferenceEquals(args.SelectedItem, NotesNavigationItem)) NavigateToNotes();
        else if (args.SelectedItem is NavigationViewItem { Tag: TaskViewDescriptor view })
        {
            var selectedTaskId = _pendingSelectedTaskId;
            _pendingSelectedTaskId = null;
            ContentFrame.Navigate(typeof(TaskListPage), new TaskListPageParameter(view, selectedTaskId));
        }
    }

    /// <summary>
    /// Opens a note on the notes page, selecting the notes entry when another page is shown.
    /// </summary>
    private void OnNoteNavigationManagerNoteOpenRequested(object? _, Guid noteId)
    {
        _pendingNoteId = noteId;
        if (ReferenceEquals(ShellNavigationView.SelectedItem, NotesNavigationItem)) NavigateToNotes();
        else ShellNavigationView.SelectedItem = NotesNavigationItem;
    }

    private void NavigateToNotes()
    {
        var parameter = new NotesPageParameter(_pendingNoteId);
        _pendingNoteId = null;
        ContentFrame.Navigate(typeof(NotesPage), parameter);
    }

    private async void OnShellNavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, AssistantNavigationItem))
        {
            await SetAssistantOpenAsync(!_isAssistantOpen || !IsAssistantDocked());
            return;
        }

        if (IsAssistantReplacingContent()) await SetAssistantOpenAsync(false);
        if (!ReferenceEquals(args.InvokedItemContainer, NewListNavigationItem)) return;
        if (await ViewModel.CreateListAsync() is { } list && _listNavigationItems.TryGetValue(list.Id, out var navigationItem)) ShellNavigationView.SelectedItem = navigationItem;
    }

    private async void OnAssistantViewCloseRequested(object? sender, EventArgs e) => await SetAssistantOpenAsync(false);

    /// <summary>
    /// Opens the assistant pane with the requested conversation, such as the briefing conversation from My Day.
    /// </summary>
    private async void OnAssistantNavigationManagerConversationOpenRequested(object? _, Guid conversationId)
    {
        _isAssistantOpen = true;
        UpdateAssistantLayout();
        await AssistantView.ActivateAsync(conversationId);
    }

    /// <summary>
    /// Opens the assistant pane once the lists are loaded, for example from the home screen widget.
    /// </summary>
    private async void OnAssistantNavigationManagerAssistantOpenRequested(object? _, EventArgs __)
    {
        if (_areListsLoaded && _assistantNavigationManager.TryTakePendingAssistantRequest())
        {
            await SetAssistantOpenAsync(true);
        }
    }

    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAssistantLayout();

    /// <summary>
    /// Whether the content area is wide enough to show the assistant next to the current page instead of in its place.
    /// </summary>
    private bool IsAssistantDocked() => ContentGrid.ActualWidth >= AssistantPaneWidth + DockedPageMinimumWidth;

    private async Task SetAssistantOpenAsync(bool isOpen)
    {
        _isAssistantOpen = isOpen;
        UpdateAssistantLayout();
        if (isOpen) await AssistantView.ActivateAsync();
    }

    /// <summary>
    /// Docks the assistant pane on the right of wide windows; on narrow windows the pane takes the place of the page.
    /// </summary>
    private void UpdateAssistantLayout()
    {
        var isDocked = IsAssistantDocked();
        AssistantPanel.Visibility = _isAssistantOpen ? Visibility.Visible : Visibility.Collapsed;
        AssistantPanel.BorderThickness = isDocked ? new Thickness(1, 0, 0, 0) : new Thickness(0);
        AssistantColumn.Width = !_isAssistantOpen ? new GridLength(0) : isDocked ? new GridLength(AssistantPaneWidth) : new GridLength(1, GridUnitType.Star);
        MainContentColumn.Width = _isAssistantOpen && !isDocked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ContentFrame.Visibility = _isAssistantOpen && !isDocked ? Visibility.Collapsed : Visibility.Visible;
        _backNavigationManager.Update();
    }

    private void OnListsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildListNavigationItems();

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaskListModel list || !_listNavigationItems.TryGetValue(list.Id, out var navigationItem)) return;
        if (e.PropertyName == nameof(TaskListModel.DisplayName)) navigationItem.Content = list.DisplayName;
        else if (e.PropertyName == nameof(TaskListModel.IsShared)) ApplySharedState(navigationItem, list);
    }

    /// <summary>
    /// Shows shared lists with the people icon instead of the list icon, and reports the shared state to assistive technology.
    /// </summary>
    private static void ApplySharedState(NavigationViewItem navigationItem, TaskListModel list)
    {
        navigationItem.Icon = new FontIcon { Glyph = list.IsShared ? SharedListGlyph : ListGlyph };
        AutomationProperties.SetItemStatus(navigationItem, list.IsShared ? LocalizedStrings.Get("SharedListItemStatus") : string.Empty);
    }

    /// <summary>
    /// Shows the user's lists below the smart lists, keeping the selection when the selected list still exists and moving to My Day otherwise.
    /// </summary>
    private void RebuildListNavigationItems()
    {
        var selectedView = (ShellNavigationView.SelectedItem as NavigationViewItem)?.Tag as TaskViewDescriptor;
        foreach (var navigationItem in _listNavigationItems.Values)
        {
            ShellNavigationView.MenuItems.Remove(navigationItem);
            if (navigationItem.DataContext is TaskListModel previousList) previousList.PropertyChanged -= OnListPropertyChanged;
        }

        _listNavigationItems.Clear();
        TasksNavigationItem.Tag = ViewModel.DefaultList is { } defaultList ? TaskViewDescriptor.ForList(defaultList.Id) : null;

        foreach (var list in ViewModel.Lists.Where(list => !list.IsDefault))
        {
            var navigationItem = new NavigationViewItem { Content = list.DisplayName, Tag = TaskViewDescriptor.ForList(list.Id), DataContext = list };
            AutomationProperties.SetAutomationId(navigationItem, $"ListNavigationItem_{list.Id}");
            ApplySharedState(navigationItem, list);
            list.PropertyChanged += OnListPropertyChanged;
            _listNavigationItems[list.Id] = navigationItem;
            ShellNavigationView.MenuItems.Add(navigationItem);
        }

        ListsSeparator.Visibility = _listNavigationItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyOpenTaskCounts(ShellNavigationView.IsPaneOpen);
        if (selectedView?.ListId is { } selectedListId && ViewModel.DefaultList?.Id != selectedListId) ShellNavigationView.SelectedItem = _listNavigationItems.TryGetValue(selectedListId, out var selectedItem) ? selectedItem : MyDayNavigationItem;
    }
}
