using System.Globalization;
using System.Text.Json;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync;

/// <summary>
/// Synchronizes the local store with the server: sends the outbox in order, reads again the items the server refused or held back,
/// then pulls the change feed. Runs one synchronization at a time; a request during a run starts another run after it.
/// </summary>
/// <remarks>Call <see cref="SyncAsync"/> from one thread at a time.</remarks>
public sealed class SyncEngine(LocalDataService data, ISyncTransport transport)
{
    /// <summary>
    /// How often an operation is sent while the server answers 409 without <c>Retry-After</c> before it is dropped.
    /// </summary>
    public const int MaximumConflictAttempts = 3;

    private TaskCompletionSource<SyncOutcome>? _currentRun;
    private bool _isRunRequested;

    public bool IsSyncing => _currentRun is { Task.IsCompleted: false };

    /// <summary>
    /// Sends pending changes and pulls the server's changes. When a run is already in progress, another run follows it and the returned task completes with it.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task<SyncOutcome> SyncAsync(CancellationToken cancellationToken)
    {
        if (_currentRun is { Task.IsCompleted: false } currentRun)
        {
            _isRunRequested = true;
            return currentRun.Task;
        }

        var run = new TaskCompletionSource<SyncOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _currentRun = run;
        _ = RunAsync(run, cancellationToken);
        return run.Task;
    }

    /// <summary>
    /// Decides what an answer of the server means for an operation.
    /// </summary>
    internal static OperationResult Classify(OutboxOperation operation, SyncTransportResponse response) => response.StatusCode switch
    {
        >= 200 and < 300 => OperationResult.Acknowledged,
        404 when operation.Method == HttpMethod.Delete.Method => OperationResult.Acknowledged,
        409 when response.HasRetryAfter => OperationResult.RetryLater,
        409 => operation.Attempts + 1 >= MaximumConflictAttempts ? OperationResult.Rejected : OperationResult.RetryLater,
        401 or 408 or 425 or 429 => OperationResult.RetryLater,
        >= 500 => OperationResult.RetryLater,
        >= 400 => OperationResult.Rejected,
        _ => OperationResult.RetryLater,
    };

    private async Task RunAsync(TaskCompletionSource<SyncOutcome> run, CancellationToken cancellationToken)
    {
        try
        {
            SyncOutcome outcome;
            do
            {
                _isRunRequested = false;
                outcome = await SyncOnceAsync(cancellationToken);
            }
            while (outcome == SyncOutcome.Completed && _isRunRequested);

            run.SetResult(outcome);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested) { run.SetCanceled(exception.CancellationToken); }
        catch (Exception exception) { run.SetException(exception); }
    }

    private async Task<SyncOutcome> SyncOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sendOutcome = await SendOutboxAsync(cancellationToken);
            if (sendOutcome != SyncOutcome.Completed) return sendOutcome;

            var refreshOutcome = await RefreshQueuedItemsAsync(cancellationToken);
            if (refreshOutcome != SyncOutcome.Completed) return refreshOutcome;

            var since = data.Store.Version;
            var changes = await transport.GetChangesAsync(since, cancellationToken);
            data.ApplyRemoteChanges(changes, isFullSnapshot: since == 0 || changes.ResetRequired);
            data.MarkSynchronized();
            return SyncOutcome.Completed;
        }
        catch (Exception exception) when (IsConnectionFailure(exception, cancellationToken)) { return SyncOutcome.Offline; }
        catch (SyncServerException exception) { return GetOutcome(exception.StatusCode); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException) { return SyncOutcome.RetryLater; }
    }

    private async Task<SyncOutcome> SendOutboxAsync(CancellationToken cancellationToken)
    {
        while (data.Store.PeekOperation() is { } operation)
        {
            SyncTransportResponse response;
            try { response = await transport.SendAsync(operation, cancellationToken); }
            catch (Exception exception) when (IsConnectionFailure(exception, cancellationToken))
            {
                data.RecordOperationFailure(operation, exception.Message);
                throw;
            }

            switch (Classify(operation, response))
            {
                case OperationResult.Acknowledged:
                    data.AcknowledgeOperation(operation, response.IsSuccess ? response.Body : null);
                    break;
                case OperationResult.Rejected:
                    data.RejectOperation(operation);
                    break;
                default:
                    data.RecordOperationFailure(operation, Describe(response));
                    return GetOutcome(response.StatusCode);
            }
        }

        return SyncOutcome.Completed;
    }

    private async Task<SyncOutcome> RefreshQueuedItemsAsync(CancellationToken cancellationToken)
    {
        foreach (var item in data.Store.GetRefreshQueue())
        {
            if (data.Store.HasPendingOperations(item.EntityType, item.EntityId)) continue;

            var response = await transport.GetAsync(GetItemPath(item), cancellationToken);
            if (!data.ApplyRefreshedItem(item, response)) return GetOutcome(response.StatusCode);
        }

        return SyncOutcome.Completed;
    }

    private static string GetItemPath(RefreshItem item) => item.EntityType switch
    {
        EntityTypes.List => $"api/lists/{item.EntityId}",
        EntityTypes.Task => $"api/tasks/{item.EntityId}",
        EntityTypes.Note => $"api/notes/{item.EntityId}",
        _ => throw new InvalidOperationException($"The entity type {item.EntityType} is unknown."),
    };

    private static SyncOutcome GetOutcome(int statusCode) => statusCode is 502 or 503 or 504 ? SyncOutcome.Offline : SyncOutcome.RetryLater;

    private static string Describe(SyncTransportResponse response) => response.Body is { Length: > 0 } body ? $"{response.StatusCode.ToString(CultureInfo.InvariantCulture)}: {body}" : response.StatusCode.ToString(CultureInfo.InvariantCulture);

    private static bool IsConnectionFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException or TimeoutException or IOException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
