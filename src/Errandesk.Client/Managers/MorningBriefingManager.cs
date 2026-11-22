using Errandesk.Client.Helpers;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.Managers;

/// <summary>
/// Holds today's morning briefing for the panel on My Day. The briefing is read from the server when My Day opens and after synchronizations,
/// and a briefing the user dismissed stays hidden on this device.
/// </summary>
public sealed class MorningBriefingManager
{
    private static readonly TimeSpan s_minimumRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly AuthenticatedApiClient _apiClient;
    private readonly ApplicationSettingsService _settingsService;
    private readonly SyncManager _syncManager;
    private readonly SessionManager _sessionManager;
    private DateTimeOffset _lastRefreshedAt = DateTimeOffset.MinValue;
    private Guid? _briefingUserId;
    private bool _isRefreshing;

    public MorningBriefingManager(AuthenticatedApiClient apiClient, ApplicationSettingsService settingsService, SyncManager syncManager, SessionManager sessionManager)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _syncManager = syncManager;
        _sessionManager = sessionManager;
        _briefingUserId = sessionManager.CurrentUser?.Id;
        _syncManager.StatusChanged += OnSyncManagerStatusChanged;
        _sessionManager.CurrentUserChanged += OnSessionManagerCurrentUserChanged;
    }

    /// <summary>
    /// Raised on the UI thread when <see cref="Briefing"/> changes.
    /// </summary>
    public event EventHandler? BriefingChanged;

    /// <summary>
    /// Today's briefing when one was written and not dismissed on this device; otherwise <see langword="null"/>.
    /// </summary>
    public MorningBriefingResponse? Briefing { get; private set; }

    /// <summary>
    /// Reads the latest briefing from the server. Without <paramref name="isForced"/>, reads at most once a minute.
    /// When the server cannot be reached, the shown briefing stays as it is.
    /// </summary>
    public async Task RefreshAsync(bool isForced)
    {
        if (_isRefreshing || (!isForced && DateTimeOffset.UtcNow - _lastRefreshedAt < s_minimumRefreshInterval)) return;

        _isRefreshing = true;
        _lastRefreshedAt = DateTimeOffset.UtcNow;
        try { SetBriefing(await _apiClient.GetLatestMorningBriefingAsync(CancellationToken.None)); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception) || exception is InvalidOperationException) { }
        finally { _isRefreshing = false; }
    }

    /// <summary>
    /// Hides the shown briefing on this device. A later briefing is shown again.
    /// </summary>
    public void Dismiss()
    {
        if (Briefing is not { } briefing) return;
        _settingsService.DismissedBriefingId = briefing.MessageId;
        SetBriefing(null);
    }

    private void SetBriefing(MorningBriefingResponse? latest)
    {
        var briefing = latest is not null && latest.Date == DateDisplayHelper.Today && latest.MessageId != _settingsService.DismissedBriefingId ? latest : null;
        if (briefing == Briefing) return;
        Briefing = briefing;
        BriefingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSyncManagerStatusChanged(object? _, EventArgs __)
    {
        if (_syncManager.IsOpen && _syncManager.IsOnline && !_syncManager.IsSyncing)
        {
            _ = RefreshAsync(isForced: false);
        }
    }

    /// <summary>
    /// Forgets the briefing when another user signs in or the user signs out; profile changes of the same user keep it.
    /// </summary>
    private void OnSessionManagerCurrentUserChanged(object? _, EventArgs __)
    {
        var userId = _sessionManager.CurrentUser?.Id;
        if (userId == _briefingUserId) return;
        _briefingUserId = userId;
        _lastRefreshedAt = DateTimeOffset.MinValue;
        SetBriefing(null);
    }
}
