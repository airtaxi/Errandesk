using System.Security.Cryptography;
using System.Text;
using Errandesk.Client.Helpers;
using Errandesk.Client.Services;
using Errandesk.Client.Sync;
using Errandesk.Contracts;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;

namespace Errandesk.Client.Managers;

/// <summary>
/// Keeps the signed-in user's lists, tasks and notes in a local store on this device and synchronizes it with the server:
/// changes are sent from the outbox as soon as the server can be reached, and the change feed is pulled after every change notification.
/// Request failures and network changes decide whether the app is offline; failed runs are retried after a growing delay.
/// </summary>
/// <remarks>Call every member on the UI thread; events are raised there.</remarks>
public sealed class SyncManager(SessionManager sessionManager, IHttpClientFactory httpClientFactory, ServerEventStreamClient streamClient)
{
    /// <summary>
    /// Name of the HTTP client the synchronization uses. It must carry <see cref="AuthenticationHandler"/>.
    /// </summary>
    public const string HttpClientName = "Sync";

    private const string DatabaseFolderName = "Sync";
    private static readonly TimeSpan s_maximumDelay = TimeSpan.FromSeconds(60);

    private DispatcherQueue? _dispatcherQueue;
    private LocalStore? _store;
    private LocalDataService? _data;
    private SyncEngine? _engine;
    private HttpClient? _httpClient;
    private string? _databasePath;
    private CancellationTokenSource? _listenCancellation;
    private CancellationTokenSource? _retryCancellation;
    private bool _isStarted;
    private bool _isInForeground = true;
    private bool _isSyncRequested;
    private int _retryCount;

    /// <summary>
    /// Raised after local data changed, by a change on this device or from the server.
    /// <see cref="ChangesResponse.ResetRequired"/> is <see langword="true"/> when the whole data set was replaced and views should load again.
    /// </summary>
    public event EventHandler<ChangesResponse>? DataChanged;

    /// <summary>
    /// Raised when <see cref="IsOnline"/>, <see cref="IsSyncing"/>, <see cref="PendingOperationCount"/> or <see cref="LastSyncedAt"/> may have changed.
    /// </summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// The local data of the signed-in user.
    /// </summary>
    /// <exception cref="InvalidOperationException">No local store is open.</exception>
    public LocalDataService Data => _data ?? throw new InvalidOperationException("The local store is not open.");

    public bool IsOpen => _data is not null;

    /// <summary>
    /// Whether the last request to the server succeeded. Changes are kept on this device while it is <see langword="false"/>.
    /// </summary>
    public bool IsOnline { get; private set; } = true;

    public bool IsSyncing { get; private set; }

    /// <summary>
    /// Whether data from the server has been stored at least once.
    /// </summary>
    public bool HasSynchronized => _data?.HasSynchronized == true;

    public int PendingOperationCount => _data?.PendingOperationCount ?? 0;

    public DateTimeOffset? LastSyncedAt => _data?.LastSyncedAt;

    /// <summary>
    /// Whether a local store exists on this device for <paramref name="userId"/> on <paramref name="serverAddress"/>.
    /// </summary>
    public static bool HasLocalData(Uri serverAddress, Guid userId) => File.Exists(GetDatabasePath(serverAddress, userId));

    /// <summary>
    /// Opens the local store of the signed-in user and starts synchronizing it while the app is in the foreground.
    /// </summary>
    public void Start()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Open();
        if (_isStarted) return;

        _isStarted = true;
        NetworkInformation.NetworkStatusChanged += OnNetworkInformationNetworkStatusChanged;
        UpdateListening();
        RequestSync();
    }

    /// <summary>
    /// Stops synchronizing and closes the local store. The stored data and pending changes stay for the next start.
    /// </summary>
    public void Stop()
    {
        if (_isStarted)
        {
            _isStarted = false;
            NetworkInformation.NetworkStatusChanged -= OnNetworkInformationNetworkStatusChanged;
            UpdateListening();
        }

        Close();
    }

    /// <summary>
    /// Stops synchronizing and deletes the signed-in user's local store, including changes that were not sent yet.
    /// </summary>
    public void DeleteLocalData()
    {
        var databasePath = _databasePath ?? (sessionManager.CurrentUser is { } user && sessionManager.TryGetServerAddress(out var serverAddress) ? GetDatabasePath(serverAddress, user.Id) : null);
        Stop();
        if (databasePath is null) return;

        try { LocalStore.Delete(databasePath); }
        catch (IOException) { }
    }

    /// <summary>
    /// Pauses listening for changes while the app is in the background and resumes it in the foreground.
    /// </summary>
    public void SetForeground(bool isInForeground)
    {
        _isInForeground = isInForeground;
        UpdateListening();
        if (isInForeground) RequestSync();
    }

    /// <summary>
    /// Sends pending changes and pulls the server's changes now.
    /// </summary>
    /// <returns>How the run ended, or <see langword="null"/> when no local store is open or the session ended.</returns>
    public async Task<SyncOutcome?> SyncNowAsync()
    {
        _retryCount = 0;
        CancelRetry();
        return await RunSyncAsync();
    }

    private void Open()
    {
        var user = sessionManager.CurrentUser ?? throw new InvalidOperationException("No user is signed in.");
        var serverAddress = sessionManager.ServerAddress;
        var databasePath = GetDatabasePath(serverAddress, user.Id);
        if (_data is not null && _databasePath == databasePath) return;

        Close();
        _store = LocalStore.Open(databasePath);
        _data = new LocalDataService(_store, TimeProvider.System);
        _data.Changed += OnDataChanged;
        _data.OperationQueued += OnDataOperationQueued;
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _httpClient.BaseAddress = serverAddress;
        _engine = new SyncEngine(_data, new HttpSyncTransport(_httpClient));
        _databasePath = databasePath;
        RaiseStatusChanged();
    }

    private void Close()
    {
        CancelRetry();
        _retryCount = 0;
        if (_data is not null)
        {
            _data.Changed -= OnDataChanged;
            _data.OperationQueued -= OnDataOperationQueued;
        }

        _engine = null;
        _data = null;
        _store?.Dispose();
        _store = null;
        _httpClient?.Dispose();
        _httpClient = null;
        _databasePath = null;
        IsOnline = true;
        IsSyncing = false;
        RaiseStatusChanged();
    }

    /// <summary>
    /// Starts a synchronization once the current UI work is done, so a change is stored and shown before it is sent.
    /// </summary>
    private void RequestSync()
    {
        if (_isSyncRequested || _engine is null) return;

        _isSyncRequested = true;
        void Run()
        {
            _isSyncRequested = false;
            _ = RunSyncAsync();
        }

        if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(Run)) Run();
    }

    private async Task<SyncOutcome?> RunSyncAsync()
    {
        if (_engine is not { } engine) return null;

        IsSyncing = true;
        RaiseStatusChanged();
        try
        {
            var outcome = await engine.SyncAsync(CancellationToken.None);
            if (!ReferenceEquals(engine, _engine)) return null;

            SetOnline(outcome != SyncOutcome.Offline);
            if (outcome == SyncOutcome.Completed) _retryCount = 0;
            else ScheduleRetry();
            return outcome;
        }
        catch (SessionExpiredException) { return null; }
        catch (Exception) when (!ReferenceEquals(engine, _engine)) { return null; }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or ApiException)
        {
            ScheduleRetry();
            return SyncOutcome.RetryLater;
        }
        finally
        {
            if (ReferenceEquals(engine, _engine))
            {
                IsSyncing = engine.IsSyncing;
                RaiseStatusChanged();
            }
        }
    }

    private void ScheduleRetry()
    {
        if (!_isStarted || _retryCancellation is not null) return;

        var delay = GetDelay(++_retryCount);
        var retryCancellation = new CancellationTokenSource();
        _retryCancellation = retryCancellation;
        _ = RetryAfterDelayAsync(delay, retryCancellation);
    }

    private async Task RetryAfterDelayAsync(TimeSpan delay, CancellationTokenSource retryCancellation)
    {
        try { await Task.Delay(delay, retryCancellation.Token); }
        catch (OperationCanceledException) { return; }

        if (!ReferenceEquals(_retryCancellation, retryCancellation)) return;
        _retryCancellation = null;
        retryCancellation.Dispose();
        RequestSync();
    }

    private void CancelRetry()
    {
        _retryCancellation?.Cancel();
        _retryCancellation?.Dispose();
        _retryCancellation = null;
    }

    private void UpdateListening()
    {
        var shouldListen = _isStarted && _isInForeground;
        if (shouldListen && _listenCancellation is null)
        {
            _listenCancellation = new CancellationTokenSource();
            _ = ListenAsync(_listenCancellation.Token);
        }
        else if (!shouldListen && _listenCancellation is not null)
        {
            _listenCancellation.Cancel();
            _listenCancellation.Dispose();
            _listenCancellation = null;
        }
    }

    /// <summary>
    /// Listens to the server's change notifications and synchronizes after each connection and each newer change. A lost connection is opened again after a growing delay.
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await streamClient.OpenChangeEventStreamAsync(cancellationToken);
                failureCount = 0;
                SetOnline(true);
                RequestSync();
                await foreach (var item in ServerEventStreamClient.ReadEventsAsync(response, cancellationToken))
                {
                    if (item.EventType != ServerEventNames.Changed || ServerEventStreamClient.ParseData(item, ErrandeskJsonSerializerContext.Default.ChangedEventData) is not { } changed) continue;
                    if (_data is not { } data || changed.Version > data.Store.Version) RequestSync();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (SessionExpiredException) { return; }
            catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception) || exception is IOException or ObjectDisposedException)
            {
                if (exception is HttpRequestException or TaskCanceledException || exception is ApiException { StatusCode: System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout })
                {
                    SetOnline(false);
                }
            }

            if (cancellationToken.IsCancellationRequested) return;
            failureCount++;
            try { await Task.Delay(GetDelay(failureCount), cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Reconnects and synchronizes right away when the network changes, instead of waiting for the next retry.
    /// </summary>
    private void OnNetworkInformationNetworkStatusChanged(object sender)
    {
        void Reconnect()
        {
            if (!_isStarted) return;

            if (_listenCancellation is not null)
            {
                _listenCancellation.Cancel();
                _listenCancellation.Dispose();
                _listenCancellation = null;
            }

            UpdateListening();
            _retryCount = 0;
            CancelRetry();
            RequestSync();
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess) Reconnect();
        else _dispatcherQueue.TryEnqueue(Reconnect);
    }

    private void OnDataChanged(object? _, ChangesResponse changes)
    {
        DataChanged?.Invoke(this, changes);
        RaiseStatusChanged();
    }

    private void OnDataOperationQueued(object? _, EventArgs __)
    {
        RaiseStatusChanged();
        RequestSync();
    }

    private void SetOnline(bool isOnline)
    {
        if (IsOnline == isOnline) return;
        IsOnline = isOnline;
        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static TimeSpan GetDelay(int failureCount)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(failureCount - 1, 6)));
        return delay < s_maximumDelay ? delay : s_maximumDelay;
    }

    /// <summary>
    /// The database file of one user on one server, in the app's local folder. The server address is hashed into the file name.
    /// </summary>
    private static string GetDatabasePath(Uri serverAddress, Guid userId)
    {
        var serverKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serverAddress.AbsoluteUri)))[..16];
        return Path.Combine(ApplicationData.Current.LocalFolder.Path, DatabaseFolderName, $"{serverKey}-{userId:N}.db");
    }
}
