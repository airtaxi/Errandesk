using System.Collections.Specialized;
using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
#if WINDOWS
using Microsoft.Windows.Storage.Pickers;
#else
using Windows.Storage.Pickers;
#endif

namespace Errandesk.Client.Views;

/// <summary>
/// The assistant pane: conversations, streamed replies with tool activity and approval cards, and the message box.
/// Enter sends the message and Shift+Enter starts a new line. Images are added with the attach button, by pasting, or by dropping files on the conversation.
/// </summary>
public sealed partial class AssistantView : UserControl
{
    private const double BottomStickDistance = 24;

    private static readonly string[] s_imageFileExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    private bool _isShiftKeyDown;
    private bool _isScrolledToBottom = true;

    public AssistantView()
    {
        InitializeComponent();
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        ChatGrid.AddHandler(DragOverEvent, new DragEventHandler(OnChatGridDragOver), handledEventsToo: true);
        ChatGrid.AddHandler(DropEvent, new DragEventHandler(OnChatGridDrop), handledEventsToo: true);
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

    /// <summary>
    /// Adds dictated text to the message box, then moves the focus to the message box with the caret after the text so the message can be edited or sent.
    /// </summary>
    private async void OnDictateButtonClicked(object sender, RoutedEventArgs e)
    {
        DictateButton.IsEnabled = false;
        try
        {
            if (!await ViewModel.DictateAsync()) return;
            MessageTextBox.Focus(FocusState.Programmatic);
            MessageTextBox.Select(MessageTextBox.Text.Length, 0);
        }
        finally { DictateButton.IsEnabled = true; }
    }

    private async void OnAttachImageButtonClicked(object sender, RoutedEventArgs e)
    {
        List<Func<Task<Stream>>> imageSources = [];
#if WINDOWS
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        foreach (var extension in s_imageFileExtensions) fileOpenPicker.FileTypeFilter.Add(extension);
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        var results = await fileOpenPicker.PickMultipleFilesAsync();
        foreach (var result in results) imageSources.Add(() => Task.FromResult<Stream>(File.OpenRead(result.Path)));
#else
        var fileOpenPicker = new FileOpenPicker();
        foreach (var extension in s_imageFileExtensions) fileOpenPicker.FileTypeFilter.Add(extension);
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        var files = await fileOpenPicker.PickMultipleFilesAsync();
        foreach (var file in files) imageSources.Add(file.OpenStreamForReadAsync);
#endif
        await ViewModel.AddImagesAsync(imageSources);
        MessageTextBox.Focus(FocusState.Programmatic);
    }

    private void OnRemovePendingImageButtonClicked(object sender, RoutedEventArgs e)
    {
        if (GetDataItem<PendingImageModel>(sender) is { } image)
        {
            ViewModel.RemovePendingImage(image);
        }
    }

    /// <summary>
    /// Shows a sent image at full size.
    /// </summary>
    private async void OnMessageImageButtonClicked(object sender, RoutedEventArgs e)
    {
        if (GetDataItem<MessageImageModel>(sender) is not { } image) return;
        if (await ViewModel.LoadFullImageAsync(image) is not { } imageSource) return;

        var maximumHeight = XamlRoot is { } xamlRoot ? xamlRoot.Size.Height * 0.7 : 600;
        var imageElement = new Image { Source = imageSource, Stretch = Stretch.Uniform, MaxHeight = maximumHeight };
        await App.GetService<DialogService>().ShowPanelAsync(string.Empty, imageElement, LocalizedStrings.Get("DialogCloseButtonText"), null);
    }

    /// <summary>
    /// Adds an image or image files from the clipboard to the message box instead of pasting them as text.
    /// </summary>
    private async void OnMessageTextBoxPaste(object sender, TextControlPasteEventArgs e)
    {
        DataPackageView clipboardContent;
        try { clipboardContent = Clipboard.GetContent(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return; }

        if (!clipboardContent.Contains(StandardDataFormats.Bitmap) && !clipboardContent.Contains(StandardDataFormats.StorageItems)) return;
        e.Handled = true;
        await AddImagesAsync(clipboardContent);
    }

    private void OnChatGridDragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanAddImages || (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap))) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private async void OnChatGridDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanAddImages || (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap))) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        List<Func<Task<Stream>>> imageSources;
        try { imageSources = await GetImageSourcesAsync(e.DataView); }
        finally { deferral.Complete(); }

        await ViewModel.AddImagesAsync(imageSources);
    }

    /// <summary>
    /// The item of a templated element. The template puts the item in <see cref="FrameworkElement.Tag"/>, which works in templates that do not set a data context.
    /// </summary>
    private static TItem? GetDataItem<TItem>(object sender) where TItem : class => sender switch
    {
        FrameworkElement { Tag: TItem item } => item,
        FrameworkElement { DataContext: TItem item } => item,
        _ => null,
    };

    private async Task AddImagesAsync(DataPackageView content) => await ViewModel.AddImagesAsync(await GetImageSourcesAsync(content));

    /// <summary>
    /// Openers for the image files in <paramref name="content"/>, or for its bitmap when it holds no files. Files that are not images are left out.
    /// </summary>
    private async Task<List<Func<Task<Stream>>>> GetImageSourcesAsync(DataPackageView content)
    {
        List<Func<Task<Stream>>> imageSources = [];
        try
        {
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await content.GetStorageItemsAsync())
                {
                    if (item is StorageFile file && s_imageFileExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
                    {
                        imageSources.Add(file.OpenStreamForReadAsync);
                    }
                }
            }
            else if (content.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await content.GetBitmapAsync();
                imageSources.Add(async () => (await bitmap.OpenReadAsync()).AsStreamForRead());
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ViewModel.ReportUnreadableImages(1); }

        return imageSources;
    }

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
