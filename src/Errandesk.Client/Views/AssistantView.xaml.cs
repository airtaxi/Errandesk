using System.Collections.Specialized;
using Errandesk.Client.Models;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Errandesk.Client.Views;

/// <summary>
/// The assistant pane: conversations, streamed replies with tool activity and approval cards, and the message box.
/// Enter sends the message and Shift+Enter starts a new line.
/// </summary>
public sealed partial class AssistantView : UserControl
{
    private const double BottomStickDistance = 24;

    private bool _isShiftKeyDown;
    private bool _isScrolledToBottom = true;

    public AssistantView()
    {
        InitializeComponent();
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
    }

    /// <summary>
    /// Raised when the user closes the pane.
    /// </summary>
    public event EventHandler? CloseRequested;

    public AssistantViewModel ViewModel { get; } = App.GetService<AssistantViewModel>();

    /// <summary>
    /// Loads the conversations the first time the pane opens and moves the focus to the message box.
    /// </summary>
    public async Task ActivateAsync()
    {
        await ViewModel.EnsureLoadedAsync();
        if (ViewModel.IsChatVisible) MessageTextBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Opens a conversation, loading the conversations first when the pane opens for the first time, and moves the focus to the message box.
    /// </summary>
    public async Task ActivateAsync(Guid conversationId)
    {
        _isScrolledToBottom = true;
        await ViewModel.ShowConversationAsync(conversationId);
        if (ViewModel.IsChatVisible) MessageTextBox.Focus(FocusState.Programmatic);
    }

    private void OnAssistantCloseButtonClicked(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Keeps the focus in the message box, because the Send and Stop buttons replace each other and a hidden button loses the focus.
    /// </summary>
    private void OnAssistantSendOrStopButtonClicked(object sender, RoutedEventArgs e) => MessageTextBox.Focus(FocusState.Programmatic);

    private async void OnConversationListViewItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ConversationItemModel conversation) return;
        _isScrolledToBottom = true;
        await ViewModel.OpenConversationAsync(conversation);
    }

    private async void OnDeleteConversationButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConversationItemModel conversation })
        {
            await ViewModel.DeleteConversationAsync(conversation);
        }
    }

    private async void OnToolUndoButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolActivityModel toolActivity })
        {
            await ViewModel.UndoAsync(toolActivity);
        }
    }

    private async void OnApproveButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApprovalModel approval })
        {
            await ViewModel.DecideApprovalAsync(approval, isApproved: true);
        }
    }

    private async void OnDenyButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApprovalModel approval })
        {
            await ViewModel.DecideApprovalAsync(approval, isApproved: false);
        }
    }

    private void OnMessageTextBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Shift)
        {
            _isShiftKeyDown = true;
            return;
        }

        if (e.Key != VirtualKey.Enter || _isShiftKeyDown) return;
        e.Handled = true;
        ViewModel.InputText = MessageTextBox.Text;
        if (ViewModel.SendCommand.CanExecute(null))
        {
            _isScrolledToBottom = true;
            ViewModel.SendCommand.Execute(null);
        }
    }

    private void OnMessageTextBoxPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Shift)
        {
            _isShiftKeyDown = false;
        }
    }

    private void OnMessageTextBoxLostFocus(object sender, RoutedEventArgs e) => _isShiftKeyDown = false;

    private void OnMessagesScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _isScrolledToBottom = MessagesScrollViewer.VerticalOffset >= MessagesScrollViewer.ScrollableHeight - BottomStickDistance;
        }
    }

    /// <summary>
    /// Keeps the newest message in view when the viewport gets shorter, such as when the on-screen keyboard opens, unless the user scrolled up to read earlier messages.
    /// </summary>
    private void OnMessagesScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isScrolledToBottom)
        {
            ScrollToBottom();
        }
    }

    /// <summary>
    /// Keeps the newest message in view while a reply grows, unless the user scrolled up to read earlier messages.
    /// </summary>
    private void OnMessagesItemsControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isScrolledToBottom)
        {
            ScrollToBottom();
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var isUserMessageAdded = e.Action == NotifyCollectionChangedAction.Add && e.NewItems?[0] is ChatMessageModel { IsUser: true };
        if (e.Action == NotifyCollectionChangedAction.Reset || isUserMessageAdded || (e.Action == NotifyCollectionChangedAction.Add && _isScrolledToBottom))
        {
            _isScrolledToBottom = true;
            DispatcherQueue.TryEnqueue(ScrollToBottom);
        }
    }

    private void ScrollToBottom()
    {
        MessagesScrollViewer.UpdateLayout();
        MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null, disableAnimation: true);
    }
}
