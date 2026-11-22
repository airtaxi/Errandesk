using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Runs every registered <see cref="IMongoIndexCreator"/> at startup, retrying with backoff until MongoDB is reachable.
/// </summary>
public sealed partial class MongoIndexInitializer(IMongoDatabase database, IEnumerable<IMongoIndexCreator> indexCreators, ILogger<MongoIndexInitializer> logger) : BackgroundService
{
    private static readonly TimeSpan s_initialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_maximumRetryDelay = TimeSpan.FromMinutes(1);

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once every index creator has finished successfully.</summary>
    public Task Completion => _completion.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = s_initialRetryDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var indexCreator in indexCreators) await indexCreator.CreateIndexesAsync(database, stoppingToken);
                LogIndexesReady(logger);
                _completion.TrySetResult();
                return;
            }
            catch (Exception exception) when (exception is MongoException or TimeoutException) { LogIndexCreationFailed(logger, exception, retryDelay); }

            await Task.Delay(retryDelay, stoppingToken);
            retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, s_maximumRetryDelay.Ticks));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MongoDB indexes are ready.")]
    private static partial void LogIndexesReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating MongoDB indexes failed. Retrying in {RetryDelay}.")]
    private static partial void LogIndexCreationFailed(ILogger logger, Exception exception, TimeSpan retryDelay);
}
