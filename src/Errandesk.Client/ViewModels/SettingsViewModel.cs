using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;
using Windows.ApplicationModel.DataTransfer;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The settings page: profile, sign-out, server, synchronization, language, theme, app updates, the morning briefing, the Microsoft To Do import, personal access tokens and, for administrators, invite codes.
/// </summary>
public sealed partial class SettingsViewModel(SessionManager sessionManager, AuthenticatedApiClient apiClient, ErrandeskApiClient anonymousApiClient, ApplicationSettingsService settingsService, AppearanceService appearanceService, AppNavigationManager appNavigationManager, DialogService dialogService, SyncManager syncManager, MicrosoftTodoImportManager importManager, ClientUpdateManager clientUpdateManager) : ObservableObject
{
    private readonly ClientUpdateManager _clientUpdateManager = clientUpdateManager;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly SyncManager _syncManager = syncManager;
    private readonly AuthenticatedApiClient _apiClient = apiClient;
    private readonly ErrandeskApiClient _anonymousApiClient = anonymousApiClient;
    private readonly AppearanceService _appearanceService = appearanceService;
    private readonly AppNavigationManager _appNavigationManager = appNavigationManager;
    private readonly DialogService _dialogService = dialogService;
    private const string BriefingTimeFormat = "HH:mm";

    private readonly MicrosoftTodoImportManager _importManager = importManager;
    private bool _isInitialized;
    private bool _isApplyingMorningBriefingSettings;
    private bool _areInvitesLoaded;
    private bool _isLoadingInvites;
    private bool _isServerInfoLoaded;
    private bool _isLoadingServerInfo;
    private bool _areAccessTokensLoaded;
    private bool _isLoadingAccessTokens;
    private Guid? _newAccessTokenId;
    private Uri? _clientUpdateBaseUrl;
    private bool _isApplyingClientUpdateState;

    public ObservableCollection<InviteModel> Invites { get; } = [];

    public ObservableCollection<PersonalAccessTokenModel> AccessTokens { get; } = [];

    /// <summary>Address of the MCP endpoint that accepts personal access tokens.</summary>
    [ObservableProperty]
    public partial string McpEndpointText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccessTokenName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCreateAccessTokenEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAccessTokensEmptyTextVisible { get; set; }

    /// <summary>The secret value of the token created last; shown until the user dismisses it or leaves the page.</summary>
    [ObservableProperty]
    public partial string NewAccessToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNewAccessTokenVisible { get; set; }

    [ObservableProperty]
    public partial string UserNameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerAddressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ThemeIndex { get; set; } = (int)settingsService.ThemePreference;

    [ObservableProperty]
    public partial int LanguageIndex { get; set; } = (int)settingsService.LanguagePreference;

    [ObservableProperty]
    public partial bool IsRestartNoticeVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAdministrator { get; set; }

    [ObservableProperty]
    public partial bool IsInvitesEmptyTextVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDisplayNameSavedVisible { get; set; }

    /// <summary>
    /// Whether the app is offline, synchronizing, waiting to send changes or up to date.
    /// </summary>
    [ObservableProperty]
    public partial string SyncStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastSyncedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLastSyncedVisible { get; set; }

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Whether the server can import from Microsoft To Do.</summary>
    [ObservableProperty]
    public partial bool IsImportAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsImportStartEnabled { get; set; }

    /// <summary>Whether the server has an AI model, which writes the morning briefings.</summary>
    [ObservableProperty]
    public partial bool IsAiAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsMorningBriefingEnabled { get; set; }

    /// <summary>Local time of the morning briefing in the profile's time zone.</summary>
    [ObservableProperty]
    public partial TimeSpan MorningBriefingTime { get; set; }

    [ObservableProperty]
    public partial string MorningBriefingTimeZoneText { get; set; } = string.Empty;

    /// <summary>Whether the import waits for the user to sign in with the code.</summary>
    [ObservableProperty]
    public partial bool IsImportCodeVisible { get; set; }

    [ObservableProperty]
    public partial string ImportUserCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Uri? ImportVerificationUri { get; set; }

    [ObservableProperty]
    public partial string ImportVerificationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportExpiresText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsImportStatusVisible { get; set; }

    [ObservableProperty]
    public partial string ImportStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsImportInProgress { get; set; }

    [ObservableProperty]
    public partial bool IsImportCountsVisible { get; set; }

    [ObservableProperty]
    public partial string ImportListsCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportTasksCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportStepsCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportSkippedCountText { get; set; } = string.Empty;

    /// <summary>Whether this platform installs updates and the server publishes them.</summary>
    [ObservableProperty]
    public partial bool IsAppUpdateSupported { get; set; }

    [ObservableProperty]
    public partial string CurrentVersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingForUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsCheckForUpdateEnabled { get; set; } = true;

    /// <summary>Whether the result of the last update check or installation is shown.</summary>
    [ObservableProperty]
    public partial bool IsAppUpdateInfoBarOpen { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity AppUpdateInfoBarSeverity { get; set; }

    [ObservableProperty]
    public partial string AppUpdateInfoBarTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AppUpdateInfoBarMessage { get; set; } = string.Empty;

    /// <summary>Whether the button that downloads and installs the newer version is shown.</summary>
    [ObservableProperty]
    public partial bool IsInstallUpdateVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingUpdate { get; set; }

    [ObservableProperty]
    public partial string UpdateDownloadProgressText { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var user = _sessionManager.CurrentUser;
        UserNameText = user?.UserName ?? string.Empty;
        DisplayName = user?.DisplayName ?? string.Empty;
        IsAdministrator = user?.IsAdministrator == true;
        ApplyMorningBriefingSettings(user);
        var hasServerAddress = _sessionManager.TryGetServerAddress(out var serverAddress);
        ServerAddressText = hasServerAddress ? ServerAddressHelper.ToDisplayText(serverAddress) : string.Empty;
        McpEndpointText = hasServerAddress ? new Uri(serverAddress, PersonalAccessTokenFormat.McpEndpointPath).AbsoluteUri : string.Empty;
        CurrentVersionText = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AppCurrentVersionFormat"), ClientUpdateService.FormatVersion(ClientUpdateService.CurrentVersion));
        _syncManager.StatusChanged -= OnSyncManagerStatusChanged;
        _syncManager.StatusChanged += OnSyncManagerStatusChanged;
        _importManager.JobChanged -= OnImportManagerJobChanged;
        _importManager.JobChanged += OnImportManagerJobChanged;
        _clientUpdateManager.StateChanged -= OnClientUpdateManagerStateChanged;
        _clientUpdateManager.StateChanged += OnClientUpdateManagerStateChanged;
        UpdateSyncStatus();
        UpdateImportState();
        ApplyClientUpdateState();
        _isInitialized = true;

        if (_syncManager.IsOnline) _ = LoadServerInfoAsync();
        if (_syncManager.IsOnline) _ = LoadAccessTokensAsync();
        if (IsAdministrator && _syncManager.IsOnline) await LoadInvitesAsync();
    }

    /// <summary>
    /// Stops following the synchronization status, the import and the app update after the page was left, and forgets the value of a newly created token.
    /// A running update download continues.
    /// </summary>
    public void Unload()
    {
        _syncManager.StatusChanged -= OnSyncManagerStatusChanged;
        _importManager.JobChanged -= OnImportManagerJobChanged;
        _clientUpdateManager.StateChanged -= OnClientUpdateManagerStateChanged;
        DismissNewAccessToken();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_isInitialized && Enum.IsDefined((ThemePreference)value))
        {
            _appearanceService.SetTheme((ThemePreference)value);
        }
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_isInitialized || !Enum.IsDefined((LanguagePreference)value)) return;
        var languagePreference = (LanguagePreference)value;
        _appearanceService.SetLanguage(languagePreference);
        IsRestartNoticeVisible = true;
        _ = SaveUserLanguageAsync(AppearanceService.GetUserLanguage(languagePreference));
    }

    partial void OnDisplayNameChanged(string value) => IsDisplayNameSavedVisible = false;

    partial void OnIsMorningBriefingEnabledChanged(bool value)
    {
        if (_isInitialized && !_isApplyingMorningBriefingSettings)
        {
            _ = SaveMorningBriefingAsync(new UpdateUserRequest { IsMorningBriefingEnabled = value });
        }
    }

    partial void OnMorningBriefingTimeChanged(TimeSpan value)
    {
        if (_isInitialized && !_isApplyingMorningBriefingSettings)
        {
            _ = SaveMorningBriefingAsync(new UpdateUserRequest { MorningBriefingTime = FormatBriefingTime(value) });
        }
    }

    /// <summary>
    /// Saves a change of the morning briefing settings. When the change fails, the settings shown return to the saved ones.
    /// </summary>
    private async Task SaveMorningBriefingAsync(UpdateUserRequest request)
    {
        IsErrorVisible = false;
        try { _sessionManager.UpdateCurrentUser(await _apiClient.UpdateCurrentUserAsync(request, CancellationToken.None)); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        ApplyMorningBriefingSettings(_sessionManager.CurrentUser);
    }

    private void ApplyMorningBriefingSettings(UserResponse? user)
    {
        var settings = user?.MorningBriefing ?? MorningBriefingSettings.Default;
        _isApplyingMorningBriefingSettings = true;
        IsMorningBriefingEnabled = settings.IsEnabled;
        MorningBriefingTime = TimeOnly.TryParseExact(settings.Time, BriefingTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.ToTimeSpan() : new TimeSpan(7, 0, 0);
        _isApplyingMorningBriefingSettings = false;
        MorningBriefingTimeZoneText = user is null ? string.Empty : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("MorningBriefingTimeZoneFormat"), user.TimeZone);
    }

    private static string FormatBriefingTime(TimeSpan value) => TimeOnly.FromTimeSpan(new TimeSpan(value.Hours, value.Minutes, 0)).ToString(BriefingTimeFormat, CultureInfo.InvariantCulture);

    [RelayCommand]
    private async Task SaveDisplayNameAsync()
    {
        var displayName = DisplayName.Trim();
        if (displayName.Length == 0) return;

        IsErrorVisible = false;
        try
        {
            var user = await _apiClient.UpdateCurrentUserAsync(new UpdateUserRequest { DisplayName = displayName }, CancellationToken.None);
            _sessionManager.UpdateCurrentUser(user);
            DisplayName = user.DisplayName;
            IsDisplayNameSavedVisible = true;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    [RelayCommand]
    private async Task SyncNowAsync() => await _syncManager.SyncNowAsync();

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (await ConfirmLosingUnsyncedChangesAsync())
        {
            await _appNavigationManager.SignOutAsync();
        }
    }

    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("ChangeServerDialogTitle"), LocalizedStrings.Get("ChangeServerDialogMessage"), LocalizedStrings.Get("ChangeServerConfirmButtonText"))) return;
        if (await ConfirmLosingUnsyncedChangesAsync()) await _appNavigationManager.ChangeServerAsync();
    }

    /// <summary>
    /// Signing out deletes the data stored on this device. Sends pending changes first and asks before changes that could not be sent are lost.
    /// </summary>
    private async Task<bool> ConfirmLosingUnsyncedChangesAsync()
    {
        if (_syncManager.PendingOperationCount == 0) return true;

        await _syncManager.SyncNowAsync();
        var pendingOperationCount = _syncManager.PendingOperationCount;
        if (pendingOperationCount == 0) return true;

        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("UnsyncedChangesDialogMessage"), pendingOperationCount);
        return await _dialogService.ConfirmAsync(LocalizedStrings.Get("UnsyncedChangesDialogTitle"), message, LocalizedStrings.Get("UnsyncedChangesConfirmButtonText"));
    }

    private void OnSyncManagerStatusChanged(object? _, EventArgs __) => UpdateSyncStatus();

    private void UpdateSyncStatus()
    {
        var pendingOperationCount = _syncManager.PendingOperationCount;
        IsSyncing = _syncManager.IsSyncing;
        SyncStatusText = !_syncManager.IsOnline ? LocalizedStrings.Get("SyncOfflineText") : IsSyncing ? LocalizedStrings.Get("SyncInProgressText") : pendingOperationCount > 0 ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(pendingOperationCount == 1 ? "SyncPendingSingularFormat" : "SyncPendingFormat"), pendingOperationCount) : LocalizedStrings.Get("SyncUpToDateText");
        IsLastSyncedVisible = _syncManager.LastSyncedAt is not null;
        LastSyncedText = _syncManager.LastSyncedAt is { } lastSyncedAt ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("LastSyncedFormat"), DateDisplayHelper.FormatReminder(lastSyncedAt)) : string.Empty;
        if (_isInitialized && IsAdministrator && !_areInvitesLoaded && _syncManager.IsOnline && !_syncManager.IsSyncing) _ = LoadInvitesAsync();
        if (_isInitialized && !_isServerInfoLoaded && _syncManager.IsOnline && !_syncManager.IsSyncing) _ = LoadServerInfoAsync();
        if (_isInitialized && !_areAccessTokensLoaded && _syncManager.IsOnline && !_syncManager.IsSyncing) _ = LoadAccessTokensAsync();
        IsImportStartEnabled = _syncManager.IsOnline && !MicrosoftTodoImportManager.IsActive(_importManager.CurrentJob);
        UpdateCreateAccessTokenEnabled();
    }

    partial void OnAccessTokenNameChanged(string value) => UpdateCreateAccessTokenEnabled();

    private void UpdateCreateAccessTokenEnabled() => IsCreateAccessTokenEnabled = _syncManager.IsOnline && !string.IsNullOrWhiteSpace(AccessTokenName);

    [RelayCommand]
    private async Task CreateAccessTokenAsync()
    {
        var name = AccessTokenName.Trim();
        if (name.Length == 0) return;

        IsErrorVisible = false;
        IsCreateAccessTokenEnabled = false;
        try
        {
            var created = await _apiClient.CreatePersonalAccessTokenAsync(new CreatePersonalAccessTokenRequest(name), CancellationToken.None);
            AccessTokens.Insert(0, new PersonalAccessTokenModel(created.PersonalAccessToken));
            IsAccessTokensEmptyTextVisible = false;
            AccessTokenName = string.Empty;
            _newAccessTokenId = created.PersonalAccessToken.Id;
            NewAccessToken = created.Token;
            IsNewAccessTokenVisible = true;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        finally { UpdateCreateAccessTokenEnabled(); }
    }

    public void CopyNewAccessToken()
    {
        if (NewAccessToken.Length > 0)
        {
            CopyText(NewAccessToken);
        }
    }

    public void CopyMcpEndpoint()
    {
        if (McpEndpointText.Length > 0)
        {
            CopyText(McpEndpointText);
        }
    }

    public void DismissNewAccessToken()
    {
        IsNewAccessTokenVisible = false;
        NewAccessToken = string.Empty;
        _newAccessTokenId = null;
    }

    public async Task RevokeAccessTokenAsync(PersonalAccessTokenModel accessToken)
    {
        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("RevokeAccessTokenDialogMessage"), accessToken.Name);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("RevokeAccessTokenDialogTitle"), message, LocalizedStrings.Get("RevokeAccessTokenConfirmButtonText"))) return;

        IsErrorVisible = false;
        try
        {
            await _apiClient.RevokePersonalAccessTokenAsync(accessToken.Id, CancellationToken.None);
            AccessTokens.Remove(accessToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { AccessTokens.Remove(accessToken); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        IsAccessTokensEmptyTextVisible = AccessTokens.Count == 0;
        if (_newAccessTokenId == accessToken.Id && !AccessTokens.Contains(accessToken)) DismissNewAccessToken();
    }

    /// <summary>
    /// Loads the personal access tokens from the server. When the server cannot be reached, the list stays as it is without an error message
    /// and is loaded again after the next synchronization reaches the server.
    /// </summary>
    private async Task LoadAccessTokensAsync()
    {
        if (_isLoadingAccessTokens) return;

        _isLoadingAccessTokens = true;
        try
        {
            var responses = await _apiClient.GetPersonalAccessTokensAsync(CancellationToken.None);
            AccessTokens.Clear();
            foreach (var response in responses) AccessTokens.Add(new PersonalAccessTokenModel(response));
            IsAccessTokensEmptyTextVisible = AccessTokens.Count == 0;
            _areAccessTokensLoaded = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        finally { _isLoadingAccessTokens = false; }
    }

    private static void CopyText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }

    [RelayCommand]
    private async Task StartImportAsync()
    {
        IsErrorVisible = false;
        try { await _importManager.StartAsync(); }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { ShowErrorMessage(LocalizedStrings.Get("ImportAlreadyRunningError")); }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.ServiceUnavailable) { ShowErrorMessage(LocalizedStrings.Get("ImportUnavailableError")); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    public void CopyImportCode()
    {
        if (ImportUserCode.Length == 0) return;
        var dataPackage = new DataPackage();
        dataPackage.SetText(ImportUserCode);
        Clipboard.SetContent(dataPackage);
    }

    private void OnImportManagerJobChanged(object? _, EventArgs __) => UpdateImportState();

    /// <summary>
    /// Shows the sign-in code while the import waits for the user, then its progress, its outcome and the counts. A cancelled import counts as no import.
    /// </summary>
    private void UpdateImportState()
    {
        var job = _importManager.CurrentJob is { Status: not ImportJobStatus.Cancelled } currentJob ? currentJob : null;
        ImportUserCode = job?.UserCode ?? string.Empty;
        IsImportCodeVisible = job?.Status == ImportJobStatus.WaitingForSignIn;
        ImportVerificationText = job?.VerificationUri ?? string.Empty;
        ImportVerificationUri = Uri.TryCreate(job?.VerificationUri, UriKind.Absolute, out var verificationUri) && verificationUri.Scheme == Uri.UriSchemeHttps ? verificationUri : null;
        ImportExpiresText = job?.ExpiresAt is { } expiresAt ? string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ImportCodeExpiresFormat"), expiresAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)) : string.Empty;
        IsImportInProgress = job?.Status == ImportJobStatus.Importing;
        ImportStatusText = job?.Status switch
        {
            ImportJobStatus.Importing => LocalizedStrings.Get("ImportInProgressText"),
            ImportJobStatus.Completed => LocalizedStrings.Get("ImportCompletedText"),
            ImportJobStatus.Failed => LocalizedStrings.Get("ImportFailedText"),
            ImportJobStatus.Expired => LocalizedStrings.Get("ImportExpiredText"),
            ImportJobStatus.Declined => LocalizedStrings.Get("ImportDeclinedText"),
            _ => string.Empty,
        };
        IsImportStatusVisible = ImportStatusText.Length > 0;
        IsImportCountsVisible = job?.Status is ImportJobStatus.Importing or ImportJobStatus.Completed or ImportJobStatus.Failed;
        ImportListsCountText = FormatCount(job?.ListsImported);
        ImportTasksCountText = FormatCount(job?.TasksImported);
        ImportStepsCountText = FormatCount(job?.StepsImported);
        ImportSkippedCountText = FormatCount(job?.TasksSkipped);
        IsImportStartEnabled = _syncManager.IsOnline && !MicrosoftTodoImportManager.IsActive(job);
    }

    private static string FormatCount(int? count) => (count ?? 0).ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Reads from the server info whether the server can import, whether it has an AI model for the morning briefing and where it publishes client updates.
    /// When the server cannot be reached, it is read again after the next synchronization reaches the server.
    /// </summary>
    private async Task LoadServerInfoAsync()
    {
        if (_isLoadingServerInfo || !_sessionManager.TryGetServerAddress(out var serverAddress)) return;

        _isLoadingServerInfo = true;
        try
        {
            var serverInfo = await _anonymousApiClient.GetServerInfoAsync(serverAddress, CancellationToken.None);
            IsImportAvailable = serverInfo.IsMicrosoftTodoImportAvailable;
            IsAiAvailable = serverInfo.IsAiAvailable;
            ApplyClientUpdateBaseUrl(serverInfo.ClientUpdateBaseUrl);
            _isServerInfoLoaded = true;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
        finally { _isLoadingServerInfo = false; }
    }

    /// <summary>
    /// Uses the client update base URL from the server info when this platform installs updates. When the server publishes updates elsewhere than the last check, the result of that check is forgotten.
    /// </summary>
    private void ApplyClientUpdateBaseUrl(string? value)
    {
        _clientUpdateBaseUrl = ClientUpdateService.IsSupported ? ClientUpdateLayout.NormalizeBaseUrl(value) : null;
        IsAppUpdateSupported = _clientUpdateBaseUrl is not null;
        if (_clientUpdateBaseUrl is null || !_clientUpdateManager.IsCheckedAgainst(_clientUpdateBaseUrl)) _clientUpdateManager.Reset();
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (_clientUpdateBaseUrl is { } baseUrl)
        {
            await _clientUpdateManager.CheckAsync(baseUrl);
        }
    }

    /// <summary>
    /// Downloads and installs the newer version found by the last check. On Windows the app exits while the installer runs; on Android the system package installer opens.
    /// </summary>
    [RelayCommand]
    private Task InstallUpdateAsync() => _clientUpdateManager.InstallAsync();

    /// <summary>
    /// Closing the update information hides the result, and cancels a running download.
    /// </summary>
    partial void OnIsAppUpdateInfoBarOpenChanged(bool value)
    {
        if (!value && !_isApplyingClientUpdateState)
        {
            _clientUpdateManager.Dismiss();
        }
    }

    private void OnClientUpdateManagerStateChanged(object? _, EventArgs __) => ApplyClientUpdateState();

    /// <summary>
    /// Shows the stage of the app update: the check, its result, the download progress and the outcome of the installation.
    /// </summary>
    private void ApplyClientUpdateState()
    {
        var state = _clientUpdateManager.State;
        var newerVersionText = _clientUpdateManager.NewerVersion is { } newerVersion ? ClientUpdateService.FormatVersion(newerVersion) : string.Empty;
        _isApplyingClientUpdateState = true;
        IsCheckingForUpdate = state == ClientUpdateState.Checking;
        IsCheckForUpdateEnabled = state is not (ClientUpdateState.Checking or ClientUpdateState.Downloading);
        IsDownloadingUpdate = state == ClientUpdateState.Downloading;
        UpdateDownloadProgressText = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AppUpdateDownloadProgressFormat"), _clientUpdateManager.DownloadPercent);
        IsInstallUpdateVisible = state is ClientUpdateState.UpdateAvailable or ClientUpdateState.Installing or ClientUpdateState.PermissionRequired or ClientUpdateState.InstallFailed;
        switch (state)
        {
            case ClientUpdateState.UpToDate:
                ShowAppUpdateInfo(InfoBarSeverity.Success, LocalizedStrings.Get("AppUpToDateTitle"), string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AppUpToDateFormat"), ClientUpdateService.FormatVersion(ClientUpdateService.CurrentVersion)));
                break;
            case ClientUpdateState.UpdateAvailable:
            case ClientUpdateState.Downloading:
                ShowAppUpdateInfo(InfoBarSeverity.Informational, LocalizedStrings.Get("AppUpdateAvailableTitle"), string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AppUpdateAvailableFormat"), newerVersionText));
                break;
            case ClientUpdateState.CheckFailed:
                ShowAppUpdateInfo(InfoBarSeverity.Error, LocalizedStrings.Get("AppUpdateCheckFailedTitle"), GetAppUpdateErrorMessage(_clientUpdateManager.LastError, true));
                break;
            case ClientUpdateState.Installing:
                ShowAppUpdateInfo(InfoBarSeverity.Informational, LocalizedStrings.Get("AppUpdateInstallingTitle"), LocalizedStrings.Get("AppUpdateInstallingMessage"));
                break;
            case ClientUpdateState.PermissionRequired:
                ShowAppUpdateInfo(InfoBarSeverity.Warning, LocalizedStrings.Get("AppUpdatePermissionRequiredTitle"), LocalizedStrings.Get("AppUpdatePermissionRequiredMessage"));
                break;
            case ClientUpdateState.InstallFailed:
                ShowAppUpdateInfo(InfoBarSeverity.Error, LocalizedStrings.Get("AppUpdateFailedTitle"), GetAppUpdateErrorMessage(_clientUpdateManager.LastError, false));
                break;
            default:
                IsAppUpdateInfoBarOpen = false;
                break;
        }

        _isApplyingClientUpdateState = false;
    }

    private void ShowAppUpdateInfo(InfoBarSeverity severity, string title, string message)
    {
        AppUpdateInfoBarSeverity = severity;
        AppUpdateInfoBarTitle = title;
        AppUpdateInfoBarMessage = message;
        IsAppUpdateInfoBarOpen = true;
    }

    /// <summary>
    /// The message for a failed update. Other input and output failures are a broken connection while checking, and a file that could not be saved while installing.
    /// </summary>
    private static string GetAppUpdateErrorMessage(Exception? exception, bool isCheck) => exception switch
    {
        HttpRequestException { StatusCode: { } statusCode } => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("AppUpdateRequestFailedFormat"), (int)statusCode),
        HttpRequestException => LocalizedStrings.Get("AppUpdateServerUnreachableError"),
        TimeoutException => LocalizedStrings.Get("AppUpdateTimeoutError"),
        InvalidDataException => LocalizedStrings.Get("AppUpdateInvalidDataError"),
        InvalidOperationException or System.ComponentModel.Win32Exception => LocalizedStrings.Get("AppUpdateLaunchError"),
        HttpIOException => LocalizedStrings.Get("AppUpdateConnectionLostError"),
        IOException when isCheck => LocalizedStrings.Get("AppUpdateConnectionLostError"),
        _ => LocalizedStrings.Get("AppUpdateFileError"),
    };

    [RelayCommand]
    private async Task CreateInviteAsync()
    {
        IsErrorVisible = false;
        try
        {
            await _apiClient.CreateInviteAsync(new CreateInviteRequest(), CancellationToken.None);
            await LoadInvitesAsync();
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    public void CopyInvite(InviteModel invite)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(invite.Code);
        Clipboard.SetContent(dataPackage);
    }

    public async Task DeleteInviteAsync(InviteModel invite)
    {
        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteInviteDialogMessage"), invite.Code);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("DeleteInviteDialogTitle"), message, LocalizedStrings.Get("DeleteButtonText"))) return;

        IsErrorVisible = false;
        try
        {
            await _apiClient.DeleteInviteAsync(invite.Id, CancellationToken.None);
            Invites.Remove(invite);
            IsInvitesEmptyTextVisible = Invites.Count == 0;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    /// <summary>
    /// Loads the invite codes from the server. When the server cannot be reached, the list stays as it is without an error message
    /// and is loaded again after the next synchronization reaches the server.
    /// </summary>
    private async Task LoadInvitesAsync()
    {
        if (_isLoadingInvites) return;

        _isLoadingInvites = true;
        try
        {
            var responses = await _apiClient.GetInvitesAsync(CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            Invites.Clear();
            foreach (var response in responses.OrderByDescending(response => response.CreatedAt)) Invites.Add(new InviteModel(response, now));
            IsInvitesEmptyTextVisible = Invites.Count == 0;
            _areInvitesLoaded = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        finally { _isLoadingInvites = false; }
    }

    private async Task SaveUserLanguageAsync(string language)
    {
        try { _sessionManager.UpdateCurrentUser(await _apiClient.UpdateCurrentUserAsync(new UpdateUserRequest { Language = language }, CancellationToken.None)); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    private void ShowError(Exception exception)
    {
        if (ApiErrorMessages.GetMessage(exception) is { } message)
        {
            ShowErrorMessage(message);
        }
    }

    private void ShowErrorMessage(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
