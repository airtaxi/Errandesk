using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.InteropServices;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The assistant pane: the conversation list, the messages of the open conversation with streamed replies,
/// tool activity with undo, approval requests, and the message box with the images to send.
/// </summary>
public sealed partial class AssistantViewModel(AuthenticatedApiClient apiClient, ServerEventStreamClient streamClient, ErrandeskApiClient serverInfoClient, SessionManager sessionManager, DialogService dialogService, AssistantNavigationManager assistantNavigationManager, ISpeechDictationService speechDictationService) : ObservableObject
{
    /// <summary>The width, in pixels, images are decoded at for the thumbnails in the message box and in messages.</summary>
    private const int ThumbnailDecodePixelWidth = 192;

    private const int ConcurrentImageDownloadLimit = 4;

    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    private readonly Dictionary<Guid, byte[]> _imageDataCache = [];
    private readonly SemaphoreSlim _imageDownloadSemaphore = new(ConcurrentImageDownloadLimit);

    private readonly AuthenticatedApiClient _apiClient = apiClient;
    private readonly ServerEventStreamClient _streamClient = streamClient;
    private readonly ErrandeskApiClient _serverInfoClient = serverInfoClient;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly DialogService _dialogService = dialogService;
    private readonly AssistantNavigationManager _assistantNavigationManager = assistantNavigationManager;
    private readonly ISpeechDictationService _speechDictationService = speechDictationService;
    private CancellationTokenSource? _replyCancellation;
    private int _conversationLoadVersion;
    private bool _isLoaded;
    private Guid? _requestedConversationId;

    public ObservableCollection<ConversationItemModel> Conversations { get; } = [];

    public ObservableCollection<ChatMessageModel> Messages { get; } = [];

    /// <summary>The images in the message box, sent with the next message.</summary>
    public ObservableCollection<PendingImageModel> PendingImages { get; } = [];

    /// <summary>
    /// The open conversation, or <see langword="null"/> for a new conversation that is created with its first message.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    public partial ConversationItemModel? CurrentConversation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatVisible), nameof(IsConversationListVisible), nameof(IsChatOrListVisible))]
    public partial bool IsAiUnavailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatVisible), nameof(IsConversationListVisible))]
    public partial bool IsConversationListOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(IsSendButtonVisible))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsReplying { get; set; }

    /// <summary>Whether images are being prepared for the message box or uploaded for a message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanAddImages))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsProcessingImages { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyConversationVisible { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyConversationListVisible { get; set; }

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether the shown error came from loading the pane, which the retry action repeats.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRetryVisible { get; set; }

    public string HeaderTitle => CurrentConversation?.DisplayTitle ?? LocalizedStrings.Get("AssistantTitle");

    public bool IsChatVisible => !IsAiUnavailable && !IsConversationListOpen;

    public bool IsConversationListVisible => !IsAiUnavailable && IsConversationListOpen;

    public bool IsChatOrListVisible => !IsAiUnavailable;

    public bool CanSend => !IsReplying && !IsProcessingImages && (!string.IsNullOrWhiteSpace(InputText) || PendingImages.Count > 0);

    public bool IsSendButtonVisible => !IsReplying;

    public bool HasPendingImages => PendingImages.Count > 0;

    public bool CanAddImages => !IsProcessingImages && PendingImages.Count < ConversationAttachmentLimits.MaximumCount;

    /// <summary>
    /// Prepares images for the message box, up to <see cref="ConversationAttachmentLimits.MaximumCount"/> images in total.
    /// Each source opens the stream of one image file; images that cannot be read are skipped and reported.
    /// </summary>
    public async Task AddImagesAsync(IReadOnlyList<Func<Task<Stream>>> imageSources)
    {
        if (imageSources.Count == 0 || IsProcessingImages) return;

        IsErrorVisible = false;
        var availableCount = ConversationAttachmentLimits.MaximumCount - PendingImages.Count;
        var failedCount = 0;
        IsProcessingImages = true;
        try
        {
            foreach (var openImage in imageSources.Take(availableCount))
            {
                try
                {
                    var image = await Task.Run(async () =>
                    {
                        await using var stream = await openImage();
                        return await ImageAttachmentEncoder.EncodeAsync(stream, CancellationToken.None);
                    });
                    PendingImages.Add(new PendingImageModel(image, await BitmapImageFactory.CreateAsync(image.Data, ThumbnailDecodePixelWidth)));
                    OnPendingImagesChanged();
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or COMException) { failedCount++; }
            }
        }
        finally { IsProcessingImages = false; }

        if (failedCount > 0) ShowError(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AssistantImageReadErrorFormat"), failedCount));
        else if (imageSources.Count > availableCount) ShowError(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AssistantImageLimitErrorFormat"), ConversationAttachmentLimits.MaximumCount));
    }

    public void RemovePendingImage(PendingImageModel image)
    {
        if (!PendingImages.Remove(image)) return;
        OnPendingImagesChanged();
    }

    /// <summary>
    /// Reports images that could not be read from the clipboard or a drop.
    /// </summary>
    public void ReportUnreadableImages(int count) => ShowError(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AssistantImageReadErrorFormat"), count));

    /// <summary>
    /// The full image of a sent message, or <see langword="null"/> when it cannot be loaded.
    /// </summary>
    public async Task<ImageSource?> LoadFullImageAsync(MessageImageModel image)
    {
        if (CurrentConversation is not { } conversation) return null;

        byte[] data;
        try { data = await GetImageDataAsync(conversation.Id, image.AttachmentId); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            HandleRequestFailure(exception);
            return null;
        }

        try { return await BitmapImageFactory.CreateAsync(data); }
        catch (COMException) { return null; }
    }

    public bool IsDictationAvailable => _speechDictationService.IsAvailable;

    /// <summary>
    /// Listens for speech and adds the recognized text to the end of the message box, separated from existing text by a space.
    /// </summary>
    /// <returns>Whether text was added.</returns>
    public async Task<bool> DictateAsync()
    {
        var text = (await _speechDictationService.DictateAsync(LocalizedStrings.Get("AssistantDictationPrompt")))?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        InputText = InputText.Length == 0 || char.IsWhiteSpace(InputText[^1]) ? InputText + text : $"{InputText} {text}";
        return true;
    }

    /// <summary>
    /// Checks whether the server has an AI model and opens the most recent conversation. Runs once; later calls do nothing.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded || IsLoading) return;
        await LoadAsync();
    }

    /// <summary>
    /// Opens the conversation with the given ID, loading the pane first when it was not loaded yet. Nothing happens when the conversation no longer exists.
    /// </summary>
    public async Task ShowConversationAsync(Guid conversationId)
    {
        _requestedConversationId = conversationId;
        if (!_isLoaded)
        {
            await EnsureLoadedAsync();
            return;
        }

        IsConversationListOpen = false;
        await OpenRequestedConversationAsync();
    }

    private async Task OpenRequestedConversationAsync()
    {
        if (_requestedConversationId is not { } conversationId) return;
        _requestedConversationId = null;
        if (CurrentConversation?.Id == conversationId) return;

        var conversation = Conversations.FirstOrDefault(item => item.Id == conversationId);
        if (conversation is null)
        {
            try { await LoadConversationsAsync(); }
            catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
            {
                HandleRequestFailure(exception);
                return;
            }

            conversation = Conversations.FirstOrDefault(item => item.Id == conversationId);
        }

        if (conversation is not null) await OpenConversationAsync(conversation);
    }

    /// <summary>
    /// Stops the reply that is streaming.
    /// </summary>
    [RelayCommand]
    public void Stop() => _replyCancellation?.Cancel();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsErrorVisible = false;
        IsLoading = true;
        try
        {
            var serverInfo = await _serverInfoClient.GetServerInfoAsync(_sessionManager.ServerAddress, CancellationToken.None);
            IsAiUnavailable = !serverInfo.IsAiAvailable;
            _assistantNavigationManager.ReportAvailability(serverInfo.IsAiAvailable);
            if (IsAiUnavailable)
            {
                _isLoaded = true;
                return;
            }

            await LoadConversationsAsync();
            _isLoaded = true;
            if (_requestedConversationId is not null) await OpenRequestedConversationAsync();
            else if (CurrentConversation is null && Messages.Count == 0 && Conversations.FirstOrDefault() is { } mostRecentConversation) await OpenConversationAsync(mostRecentConversation);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { HandleRequestFailure(exception, canRetry: true); }
        finally
        {
            IsLoading = false;
            UpdateEmptyStates();
        }
    }

    [RelayCommand]
    private void ToggleConversationList()
    {
        IsConversationListOpen = !IsConversationListOpen;
        if (IsConversationListOpen) _ = RefreshConversationsAsync();
    }

    [RelayCommand]
    private void NewConversation()
    {
        Stop();
        _conversationLoadVersion++;
        CurrentConversation = null;
        Messages.Clear();
        IsConversationListOpen = false;
        IsErrorVisible = false;
        UpdateEmptyStates();
    }

    /// <summary>
    /// Shows a conversation from the list with its history.
    /// </summary>
    public async Task OpenConversationAsync(ConversationItemModel conversation)
    {
        Stop();
        var loadVersion = ++_conversationLoadVersion;
        CurrentConversation = conversation;
        IsConversationListOpen = false;
        IsErrorVisible = false;
        Messages.Clear();
        IsLoading = true;
        try
        {
            var messages = await _apiClient.GetConversationMessagesAsync(conversation.Id, CancellationToken.None);
            if (loadVersion != _conversationLoadVersion) return;
            foreach (var message in messages)
            {
                var messageModel = new ChatMessageModel(message);
                Messages.Add(messageModel);
                foreach (var image in messageModel.Images) _ = LoadThumbnailAsync(conversation.Id, image);
            }
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            if (loadVersion == _conversationLoadVersion)
            {
                HandleRequestFailure(exception);
            }
        }
        finally
        {
            if (loadVersion == _conversationLoadVersion) IsLoading = false;
            UpdateEmptyStates();
        }
    }

    public async Task DeleteConversationAsync(ConversationItemModel conversation)
    {
        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteConversationDialogMessage"), conversation.DisplayTitle);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("DeleteConversationDialogTitle"), message, LocalizedStrings.Get("DeleteButtonText"))) return;

        IsErrorVisible = false;
        try
        {
            if (ReferenceEquals(conversation, CurrentConversation)) Stop();
            await _apiClient.DeleteConversationAsync(conversation.Id, CancellationToken.None);
            Conversations.Remove(conversation);
            if (ReferenceEquals(conversation, CurrentConversation))
            {
                _conversationLoadVersion++;
                CurrentConversation = null;
                Messages.Clear();
            }

            UpdateEmptyStates();
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { HandleRequestFailure(exception); }
    }

    /// <summary>
    /// Uploads the images in the message box, sends them with the text, and streams the reply. Approval requests still waiting for a decision are denied by the server.
    /// When the message cannot be sent, the text and the images stay in the message box.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        List<PendingImageModel> images = [.. PendingImages];
        if ((text.Length == 0 && images.Count == 0) || IsReplying || IsProcessingImages) return;

        IsErrorVisible = false;
        IsReplying = true;
        try
        {
            if (CurrentConversation is not { } conversation)
            {
                conversation = new ConversationItemModel(await _apiClient.CreateConversationAsync(new CreateConversationRequest(Id: Guid.CreateVersion7()), CancellationToken.None));
                Conversations.Insert(0, conversation);
                _conversationLoadVersion++;
                CurrentConversation = conversation;
            }

            var attachmentIds = await UploadImagesAsync(conversation.Id, images);
            InputText = string.Empty;
            PendingImages.Clear();
            OnPendingImagesChanged();
            var userMessage = new ChatMessageModel(Guid.CreateVersion7(), isUser: true, text);
            for (var index = 0; index < images.Count; index++)
            {
                _imageDataCache[attachmentIds[index]] = images[index].Image.Data;
                userMessage.Images.Add(new MessageImageModel(attachmentIds[index]) { Thumbnail = images[index].Thumbnail });
            }

            Messages.Add(userMessage);
            UpdateEmptyStates();
            var pendingApprovals = GetPendingApprovals();
            if (await StreamReplyAsync(conversation, cancellationToken => _streamClient.OpenMessageStreamAsync(conversation.Id, new SendMessageRequest(text, attachmentIds), cancellationToken)))
            {
                foreach (var approval in pendingApprovals)
                {
                    approval.Status = ApprovalStatuses.Denied;
                }
            }
            else
            {
                Messages.Remove(userMessage);
                if (InputText.Length == 0) InputText = text;
                if (PendingImages.Count == 0)
                {
                    foreach (var image in images) PendingImages.Add(image);
                    OnPendingImagesChanged();
                }

                UpdateEmptyStates();
            }
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            InputText = text;
            HandleRequestFailure(exception);
        }
        finally { IsReplying = false; }
    }

    /// <summary>
    /// Uploads the images that are not uploaded to the conversation yet and returns the attachment IDs of all of them, in order.
    /// </summary>
    private async Task<List<Guid>> UploadImagesAsync(Guid conversationId, List<PendingImageModel> images)
    {
        List<Guid> attachmentIds = [];
        if (images.Count == 0) return attachmentIds;

        IsProcessingImages = true;
        try
        {
            foreach (var image in images)
            {
                if (image.GetAttachmentId(conversationId) is not { } attachmentId)
                {
                    attachmentId = (await _apiClient.UploadConversationAttachmentAsync(conversationId, image.Image.Data, image.Image.MediaType, CancellationToken.None)).Id;
                    image.SetUploaded(conversationId, attachmentId);
                }

                attachmentIds.Add(attachmentId);
            }
        }
        finally { IsProcessingImages = false; }

        return attachmentIds;
    }

    private async Task LoadThumbnailAsync(Guid conversationId, MessageImageModel image)
    {
        try
        {
            var data = await GetImageDataAsync(conversationId, image.AttachmentId);
            image.Thumbnail = await BitmapImageFactory.CreateAsync(data, ThumbnailDecodePixelWidth);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception) || exception is COMException) { image.HasFailed = true; }
    }

    private async Task<byte[]> GetImageDataAsync(Guid conversationId, Guid attachmentId)
    {
        if (_imageDataCache.TryGetValue(attachmentId, out var cachedData)) return cachedData;

        await _imageDownloadSemaphore.WaitAsync();
        try
        {
            if (_imageDataCache.TryGetValue(attachmentId, out cachedData)) return cachedData;
            var data = await _apiClient.GetConversationAttachmentAsync(conversationId, attachmentId, CancellationToken.None);
            _imageDataCache[attachmentId] = data;
            return data;
        }
        finally { _imageDownloadSemaphore.Release(); }
    }

    private void OnPendingImagesChanged()
    {
        OnPropertyChanged(nameof(HasPendingImages));
        OnPropertyChanged(nameof(CanAddImages));
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Records the user's decision on an approval request. Once every pending request of the conversation is decided, the decisions are sent and the reply resumes.
    /// </summary>
    public async Task DecideApprovalAsync(ApprovalModel approval, bool isApproved)
    {
        if (!approval.IsPending || IsReplying || CurrentConversation is not { } conversation) return;

        approval.ChosenDecision = isApproved;
        var pendingApprovals = GetPendingApprovals();
        if (pendingApprovals.Any(pendingApproval => pendingApproval.ChosenDecision is null)) return;

        IsErrorVisible = false;
        IsReplying = true;
        foreach (var pendingApproval in pendingApprovals) pendingApproval.IsSubmitting = true;
        var request = new ApprovalDecisionsRequest((ApprovalDecision[])[.. pendingApprovals.Select(pendingApproval => new ApprovalDecision(pendingApproval.ApprovalId, pendingApproval.ChosenDecision == true))]);
        try
        {
            if (await StreamReplyAsync(conversation, cancellationToken => _streamClient.OpenApprovalStreamAsync(conversation.Id, request, cancellationToken)))
            {
                foreach (var pendingApproval in pendingApprovals)
                {
                    pendingApproval.Status = pendingApproval.ChosenDecision == true ? ApprovalStatuses.Approved : ApprovalStatuses.Denied;
                }
            }
            else
            {
                foreach (var pendingApproval in pendingApprovals)
                {
                    pendingApproval.ChosenDecision = null;
                }
            }
        }
        finally
        {
            foreach (var pendingApproval in pendingApprovals) pendingApproval.IsSubmitting = false;
            IsReplying = false;
        }
    }

    /// <summary>
    /// Restores the items a tool changed to their state before the change.
    /// </summary>
    public async Task UndoAsync(ToolActivityModel toolActivity)
    {
        if (!toolActivity.CanUndo || toolActivity.AgentActionId is not { } agentActionId) return;

        IsErrorVisible = false;
        toolActivity.IsUndoing = true;
        try
        {
            await _apiClient.UndoAgentActionAsync(agentActionId, CancellationToken.None);
            toolActivity.IsUndone = true;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            toolActivity.IsUndoUnavailable = true;
            ShowError(LocalizedStrings.Get("UndoConflictError"));
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { HandleRequestFailure(exception); }
        finally { toolActivity.IsUndoing = false; }
    }

    /// <summary>
    /// Opens a reply stream and shows its events as they arrive.
    /// </summary>
    /// <returns><see langword="true"/> when the server accepted the request and the stream started.</returns>
    private async Task<bool> StreamReplyAsync(ConversationItemModel conversation, Func<CancellationToken, Task<HttpResponseMessage>> openStream)
    {
        using var replyCancellation = new CancellationTokenSource();
        _replyCancellation = replyCancellation;
        var cancellationToken = replyCancellation.Token;
        ChatMessageModel? reply = null;
        var isStarted = false;
        try
        {
            using var response = await openStream(cancellationToken);
            isStarted = true;
            await foreach (var item in ServerEventStreamClient.ReadEventsAsync(response, cancellationToken))
            {
                if (!ReferenceEquals(conversation, CurrentConversation)) break;
                reply = ApplyStreamEvent(item, reply);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (reply is not null)
            {
                reply.IsStopped = true;
            }
        }
        catch (Exception exception) when (isStarted && (ApiErrorMessages.IsRequestFailure(exception) || exception is IOException or ObjectDisposedException))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (reply is not null)
                {
                    reply.IsStopped = true;
                }
            }
            else
            {
                reply ??= AddReply(Guid.CreateVersion7());
                reply.ErrorText = LocalizedStrings.Get("AssistantConnectionLostError");
            }
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { HandleRequestFailure(exception); }
        finally
        {
            reply?.EndStreaming();
            if (ReferenceEquals(_replyCancellation, replyCancellation)) _replyCancellation = null;
            foreach (var toolActivity in Messages.SelectMany(message => message.ToolActivities).Where(toolActivity => toolActivity.IsRunning)) toolActivity.Complete(false, null, null);
            UpdateEmptyStates();
        }

        if (isStarted) await RefreshConversationAsync(conversation);
        return isStarted;
    }

    private ChatMessageModel? ApplyStreamEvent(SseItem<string> item, ChatMessageModel? reply)
    {
        switch (item.EventType)
        {
            case ServerEventNames.MessageStarted:
                if (ServerEventStreamClient.ParseData(item, JsonContext.MessageStartedEventData) is { } started) return AddReply(started.MessageId);
                break;
            case ServerEventNames.TextDelta:
                if (ServerEventStreamClient.ParseData(item, JsonContext.TextDeltaEventData) is { } delta) (reply ??= AddReply(Guid.CreateVersion7())).AppendText(delta.Text);
                break;
            case ServerEventNames.ToolStarted:
                if (ServerEventStreamClient.ParseData(item, JsonContext.ToolStartedEventData) is { } toolStarted)
                {
                    reply ??= AddReply(Guid.CreateVersion7());
                    if (reply.FindToolActivity(toolStarted.CallId) is null) reply.ToolActivities.Add(new ToolActivityModel(toolStarted.CallId, toolStarted.ToolName));
                }

                break;
            case ServerEventNames.ToolCompleted:
                if (ServerEventStreamClient.ParseData(item, JsonContext.ToolCompletedEventData) is { } toolCompleted)
                {
                    reply ??= AddReply(Guid.CreateVersion7());
                    var toolActivity = reply.FindToolActivity(toolCompleted.CallId);
                    if (toolActivity is null)
                    {
                        toolActivity = new ToolActivityModel(toolCompleted.CallId, toolCompleted.ToolName);
                        reply.ToolActivities.Add(toolActivity);
                    }

                    toolActivity.Complete(toolCompleted.Succeeded, toolCompleted.AgentActionId, toolCompleted.Error);
                }

                break;
            case ServerEventNames.ApprovalRequested:
                if (ServerEventStreamClient.ParseData(item, JsonContext.ApprovalRequestResponse) is { } approvalRequest) (reply ??= AddReply(Guid.CreateVersion7())).Approvals.Add(new ApprovalModel(approvalRequest));
                break;
            case ServerEventNames.MessageCompleted:
                if (ServerEventStreamClient.ParseData(item, JsonContext.MessageCompletedEventData) is { } completed) (reply ??= AddReply(completed.MessageId)).Complete(completed.Text);
                break;
            case ServerEventNames.Error:
                var error = ServerEventStreamClient.ParseData(item, JsonContext.StreamErrorEventData);
                (reply ??= AddReply(Guid.CreateVersion7())).ErrorText = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AssistantReplyFailedFormat"), error?.Message ?? string.Empty);
                break;
        }

        return reply;
    }

    private ChatMessageModel AddReply(Guid messageId)
    {
        var reply = new ChatMessageModel(messageId, isUser: false, string.Empty) { IsStreaming = true };
        Messages.Add(reply);
        UpdateEmptyStates();
        return reply;
    }

    private List<ApprovalModel> GetPendingApprovals() =>
    [.. Messages.SelectMany(message => message.Approvals).Where(approval => approval.IsPending)];

    private async Task LoadConversationsAsync()
    {
        var responses = await _apiClient.GetConversationsAsync(CancellationToken.None);
        var currentConversationId = CurrentConversation?.Id;
        Conversations.Clear();
        foreach (var response in responses)
        {
            if (response.Id == currentConversationId && CurrentConversation is { } currentConversation)
            {
                currentConversation.Apply(response);
                Conversations.Add(currentConversation);
            }
            else Conversations.Add(new ConversationItemModel(response));
        }

        UpdateEmptyStates();
    }

    private async Task RefreshConversationsAsync()
    {
        try { await LoadConversationsAsync(); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { HandleRequestFailure(exception, canRetry: true); }
    }

    /// <summary>
    /// Reloads a conversation after a reply, whose first message sets the title, and moves it to the top of the list.
    /// </summary>
    private async Task RefreshConversationAsync(ConversationItemModel conversation)
    {
        try
        {
            conversation.Apply(await _apiClient.GetConversationAsync(conversation.Id, CancellationToken.None));
            if (ReferenceEquals(conversation, CurrentConversation)) OnPropertyChanged(nameof(HeaderTitle));
            var index = Conversations.IndexOf(conversation);
            if (index > 0) Conversations.Move(index, 0);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
    }

    private void UpdateEmptyStates()
    {
        IsEmptyConversationVisible = !IsLoading && Messages.Count == 0;
        IsEmptyConversationListVisible = Conversations.Count == 0;
    }

    /// <summary>
    /// Shows why a request failed. A 503 answer can mean that the AI model was removed from the server, so the server information is read again.
    /// </summary>
    private void HandleRequestFailure(Exception exception, bool canRetry = false)
    {
        if (exception is ApiException { StatusCode: HttpStatusCode.Conflict }) ShowError(LocalizedStrings.Get("AssistantBusyError"));
        else if (ApiErrorMessages.GetMessage(exception) is { } message) ShowError(message, canRetry);
        if (exception is ApiException { StatusCode: HttpStatusCode.ServiceUnavailable }) _ = CheckAvailabilityAsync();
    }

    private async Task CheckAvailabilityAsync()
    {
        try
        {
            var isAiAvailable = (await _serverInfoClient.GetServerInfoAsync(_sessionManager.ServerAddress, CancellationToken.None)).IsAiAvailable;
            _assistantNavigationManager.ReportAvailability(isAiAvailable);
            if (!isAiAvailable)
            {
                IsErrorVisible = false;
                IsAiUnavailable = true;
            }
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
    }

    private void ShowError(string message, bool canRetry = false)
    {
        ErrorMessage = message;
        IsRetryVisible = canRetry;
        IsErrorVisible = true;
    }
}
