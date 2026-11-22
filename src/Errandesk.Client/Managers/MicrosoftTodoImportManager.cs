using Errandesk.Client.Helpers;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.Managers;

/// <summary>
/// Keeps the signed-in user's Microsoft To Do import of this app session and follows its progress while it is active, so it survives leaving the settings page.
/// Its members are used on the UI thread.
/// </summary>
public sealed class MicrosoftTodoImportManager(AuthenticatedApiClient apiClient, SessionManager sessionManager)
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(2);

    private ImportJobResponse? _currentJob;
    private Guid? _jobUserId;
    private int _pollGeneration;

    /// <summary>Raised on the UI thread when <see cref="CurrentJob"/> changes.</summary>
    public event EventHandler? JobChanged;

    /// <summary>The import started in this app session by the signed-in user, or <see langword="null"/>.</summary>
    public ImportJobResponse? CurrentJob => _jobUserId is { } jobUserId && jobUserId == sessionManager.CurrentUser?.Id ? _currentJob : null;

    /// <summary>Whether a job waits for the sign-in or is importing.</summary>
    public static bool IsActive(ImportJobResponse? job) => job?.Status is ImportJobStatus.WaitingForSignIn or ImportJobStatus.Importing;

    /// <summary>
    /// Starts an import, replacing an earlier one that still waits for the sign-in, and follows it until it ends.
    /// </summary>
    /// <exception cref="ApiException">409 when an import is already running, 503 when the import is not available.</exception>
    public async Task StartAsync()
    {
        var job = await apiClient.StartMicrosoftTodoImportAsync(CancellationToken.None);
        SetJob(job);
        _ = FollowAsync(job.Id, ++_pollGeneration);
    }

    private void SetJob(ImportJobResponse job)
    {
        _jobUserId = sessionManager.CurrentUser?.Id;
        _currentJob = job;
        JobChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the job every few seconds until it ends, another import replaces it, or the user changes. Failed requests are retried at the next interval.
    /// </summary>
    private async Task FollowAsync(Guid jobId, int pollGeneration)
    {
        var userId = sessionManager.CurrentUser?.Id;
        while (pollGeneration == _pollGeneration && userId == sessionManager.CurrentUser?.Id && IsActive(CurrentJob))
        {
            await Task.Delay(s_pollInterval);
            if (pollGeneration != _pollGeneration || userId != sessionManager.CurrentUser?.Id) return;

            try { SetJob(await apiClient.GetImportJobAsync(jobId, CancellationToken.None)); }
            catch (SessionExpiredException) { return; }
            catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
        }
    }
}
