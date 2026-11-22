using System.Collections.Concurrent;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using MongoDB.Driver;

namespace Errandesk.Server.Import;

/// <summary>
/// Starts Microsoft To Do imports and runs them in the background: waits for the user to sign in with the device code, then imports with the tokens, which are kept in memory only.
/// Jobs run in this server process; a job left unfinished by an earlier process is reported as failed.
/// </summary>
public sealed class MicrosoftTodoImportService(ErrandeskDatabase database, ServerSettings settings, MicrosoftIdentityClient identityClient, IHttpClientFactory httpClientFactory, MicrosoftTodoImporter importer, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater, MicrosoftTodoImportOptions options, IHostApplicationLifetime applicationLifetime, ILogger<MicrosoftTodoImportService> logger)
{
    public const string InterruptedMessage = "The server stopped before the import finished.";

    private const string ResourceName = "import job";
    private const string UnexpectedErrorMessage = "The import stopped because of an unexpected error.";

    private static readonly ImportJobStatus[] s_activeStatuses = [ImportJobStatus.WaitingForSignIn, ImportJobStatus.Importing];
    private static readonly TimeSpan s_slowDownIncrease = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, RunningImport> _runningImports = new();

    /// <summary>
    /// Starts an import for the user and returns the job with the sign-in code. An earlier import that still waits for sign-in is cancelled; one that is importing makes this fail with 409.
    /// </summary>
    public async Task<ImportJobResponse> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (settings.MicrosoftGraph is null) throw new ImportUnavailableException("The server has no Microsoft Graph client ID configured.");
        await ReplaceActiveJobsAsync(userId, cancellationToken);

        DeviceCodeResponse deviceCode;
        try { deviceCode = await identityClient.RequestDeviceCodeAsync(cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or MicrosoftServiceException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Requesting a Microsoft device code failed.");
            throw new ImportUnavailableException("Microsoft sign-in could not be reached. Try again later.");
        }

        var utcNow = documentUpdater.GetUtcNow();
        var job = new ImportJobDocument
        {
            Id = Guid.CreateVersion7(),
            Source = ImportSources.MicrosoftTodo,
            Status = ImportJobStatus.WaitingForSignIn,
            UserCode = deviceCode.UserCode,
            VerificationUri = deviceCode.VerificationUri,
            ExpiresAt = utcNow.AddSeconds(deviceCode.ExpiresIn),
        };
        job.StampCreated(userId, utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.ImportJobs.InsertOneAsync(job, cancellationToken: cancellationToken);

        var runningImport = new RunningImport(CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime.ApplicationStopping));
        _runningImports[job.Id] = runningImport;
        _ = Task.Run(() => RunAsync(job, deviceCode, runningImport), CancellationToken.None);
        return job.ToResponse();
    }

    /// <summary>The user's job, reporting a job that no process is running anymore as failed.</summary>
    public async Task<ImportJobResponse> GetAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await database.ImportJobs.Find(job => job.Id == jobId && job.OwnerUserId == userId && !job.IsDeleted).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);
        if (s_activeStatuses.Contains(job.Status) && !_runningImports.ContainsKey(job.Id))
        {
            await CompleteAsync(job.Id, ImportJobStatus.Failed, null, InterruptedMessage, cancellationToken);
            job = await database.ImportJobs.Find(storedJob => storedJob.Id == jobId).FirstAsync(cancellationToken);
        }
        return job.ToResponse();
    }

    /// <summary>Completes when the job's background run has ended, or at once when it is not running.</summary>
    internal Task WaitForRunAsync(Guid jobId) => _runningImports.TryGetValue(jobId, out var runningImport) ? runningImport.Finished.Task : Task.CompletedTask;

    private async Task ReplaceActiveJobsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeJobs = await database.ImportJobs.Find(Builders<ImportJobDocument>.Filter.Eq(job => job.OwnerUserId, userId) & Builders<ImportJobDocument>.Filter.In(job => job.Status, s_activeStatuses)).ToListAsync(cancellationToken);
        foreach (var activeJob in activeJobs)
        {
            if (!_runningImports.TryGetValue(activeJob.Id, out var runningImport)) await CompleteAsync(activeJob.Id, ImportJobStatus.Failed, null, InterruptedMessage, cancellationToken);
            else if (activeJob.Status == ImportJobStatus.Importing) throw new ResourceConflictException("A Microsoft To Do import is already running.");
            else
            {
                runningImport.IsReplaced = true;
                await runningImport.Cancellation.CancelAsync();
                await CompleteAsync(activeJob.Id, ImportJobStatus.Cancelled, null, null, cancellationToken);
            }
        }
    }

    private async Task RunAsync(ImportJobDocument job, DeviceCodeResponse deviceCode, RunningImport runningImport)
    {
        var cancellationToken = runningImport.Cancellation.Token;
        var counts = new ImportCounts();
        try
        {
            var tokens = await WaitForSignInAsync(job, deviceCode, cancellationToken);
            if (tokens is null) return;

            await UpdateAsync(job.Id, Builders<ImportJobDocument>.Update.Set(storedJob => storedJob.Status, ImportJobStatus.Importing), cancellationToken);
            var graphClient = new MicrosoftGraphTodoClient(httpClientFactory.CreateClient(MicrosoftGraphTodoClient.HttpClientName), tokens, identityClient.RefreshAsync, options);
            await importer.ImportAsync(job.OwnerUserId, graphClient, counts, progressCancellationToken => SaveCountsAsync(job.Id, counts, progressCancellationToken), cancellationToken);
            await CompleteAsync(job.Id, ImportJobStatus.Completed, counts, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!runningImport.IsReplaced)
            {
                await TryCompleteAsync(job.Id, ImportJobStatus.Failed, counts, InterruptedMessage);
            }
        }
        catch (MicrosoftServiceException exception)
        {
            logger.LogWarning(exception, "The Microsoft To Do import {JobId} failed.", job.Id);
            await TryCompleteAsync(job.Id, ImportJobStatus.Failed, counts, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The Microsoft To Do import {JobId} failed.", job.Id);
            await TryCompleteAsync(job.Id, ImportJobStatus.Failed, counts, UnexpectedErrorMessage);
        }
        finally
        {
            _runningImports.TryRemove(job.Id, out _);
            runningImport.Cancellation.Dispose();
            runningImport.Finished.TrySetResult();
        }
    }

    /// <summary>Asks the Microsoft identity platform for the tokens until the user signs in. Returns <see langword="null"/> after recording why the sign-in ended without tokens.</summary>
    private async Task<MicrosoftTokens?> WaitForSignInAsync(ImportJobDocument job, DeviceCodeResponse deviceCode, CancellationToken cancellationToken)
    {
        var pollInterval = options.PollInterval ?? TimeSpan.FromSeconds(Math.Max(1, deviceCode.Interval));
        while (true)
        {
            if (documentUpdater.GetUtcNow() >= job.ExpiresAt)
            {
                await CompleteAsync(job.Id, ImportJobStatus.Expired, null, null, cancellationToken);
                return null;
            }

            await Task.Delay(pollInterval, cancellationToken);
            DeviceCodeTokenResult result;
            try { result = await identityClient.PollTokenAsync(deviceCode.DeviceCode!, cancellationToken); }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Checking the Microsoft sign-in of import {JobId} failed; trying again.", job.Id);
                continue;
            }

            switch (result.Status)
            {
                case DeviceCodeTokenStatus.Succeeded:
                    return result.Tokens;
                case DeviceCodeTokenStatus.SlowDown:
                    if (options.PollInterval is null) pollInterval += s_slowDownIncrease;
                    break;
                case DeviceCodeTokenStatus.Declined:
                    await CompleteAsync(job.Id, ImportJobStatus.Declined, null, null, cancellationToken);
                    return null;
                case DeviceCodeTokenStatus.Expired:
                    await CompleteAsync(job.Id, ImportJobStatus.Expired, null, null, cancellationToken);
                    return null;
            }
        }
    }

    private Task SaveCountsAsync(Guid jobId, ImportCounts counts, CancellationToken cancellationToken) => UpdateAsync(jobId, SetCounts(Builders<ImportJobDocument>.Update.Combine(), counts), cancellationToken);

    /// <summary>Moves an active job to a final status. A job that already reached a final status keeps it.</summary>
    private async Task CompleteAsync(Guid jobId, ImportJobStatus status, ImportCounts? counts, string? error, CancellationToken cancellationToken)
    {
        var update = Builders<ImportJobDocument>.Update.Set(job => job.Status, status).Set(job => job.Error, error).Set(job => job.CompletedAt, documentUpdater.GetUtcNow());
        if (counts is not null) update = SetCounts(update, counts);
        await UpdateAsync(jobId, update, cancellationToken);
    }

    private async Task TryCompleteAsync(Guid jobId, ImportJobStatus status, ImportCounts counts, string error)
    {
        try { await CompleteAsync(jobId, status, counts, error, CancellationToken.None); }
        catch (Exception exception) { logger.LogError(exception, "Recording the end of import {JobId} failed.", jobId); }
    }

    private async Task UpdateAsync(Guid jobId, UpdateDefinition<ImportJobDocument> update, CancellationToken cancellationToken)
    {
        var activeJobFilter = Builders<ImportJobDocument>.Filter.Eq(job => job.Id, jobId) & Builders<ImportJobDocument>.Filter.In(job => job.Status, s_activeStatuses);
        var stampedUpdate = update.Set(job => job.UpdatedAt, documentUpdater.GetUtcNow()).Set(job => job.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.ImportJobs.UpdateOneAsync(activeJobFilter, stampedUpdate, cancellationToken: cancellationToken);
    }

    private static UpdateDefinition<ImportJobDocument> SetCounts(UpdateDefinition<ImportJobDocument> update, ImportCounts counts) => update.Set(job => job.ListsImported, counts.ListsImported).Set(job => job.ListsSkipped, counts.ListsSkipped).Set(job => job.TasksImported, counts.TasksImported).Set(job => job.TasksSkipped, counts.TasksSkipped).Set(job => job.StepsImported, counts.StepsImported);

    private sealed class RunningImport(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether a newer import cancelled this one, which records the cancellation itself.</summary>
        public bool IsReplaced { get; set; }
    }
}
