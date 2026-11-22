using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using MongoDB.Driver;

namespace Errandesk.Server.Notifications;

/// <summary>
/// Sends due task reminders as push notifications to the mobile devices of the user who set them, as long as that user can still see the task's list.
/// Each reminder is claimed atomically before sending, so several server instances never send the same reminder twice; claims abandoned for longer than the claim timeout are taken over.
/// </summary>
public sealed class ReminderDispatcher(ErrandeskDatabase database, DeviceService deviceService, IEnumerable<IPushNotificationSender> senders, ChangeVersionGenerator changeVersionGenerator, TaskListAccess taskListAccess, DocumentUpdater documentUpdater, ReminderDispatcherOptions options, TimeProvider timeProvider, ILogger<ReminderDispatcher> logger) : BackgroundService
{
    private readonly Dictionary<string, IPushNotificationSender> _sendersByPlatform = senders.ToDictionary(sender => sender.Platform, StringComparer.Ordinal);

    /// <summary>Claims and handles due reminders one at a time until none is left or the per-check limit is reached. Returns the number of reminders handled.</summary>
    public async Task<int> DispatchDueRemindersAsync(CancellationToken cancellationToken)
    {
        var handledCount = 0;
        while (handledCount < options.MaximumRemindersPerCheck)
        {
            var claim = await ClaimNextAsync(cancellationToken);
            if (claim is null) break;

            await HandleClaimAsync(claim.Value.Task, claim.Value.ClaimId, cancellationToken);
            handledCount++;
        }
        return handledCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(options.PollInterval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try { await DispatchDueRemindersAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Dispatching reminders failed."); }
        }
    }

    /// <summary>
    /// Finds the earliest due reminder that is not sent and not claimed (or whose claim is abandoned), and claims it with a conditional update so only one caller wins.
    /// </summary>
    private async Task<(TaskDocument Task, Guid ClaimId)?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var utcNow = documentUpdater.GetUtcNow();
            var dueFilter = CreateDueFilter(utcNow);
            var candidateId = await database.Tasks.Find(dueFilter).SortBy(task => task.ReminderAt).Project(task => task.Id).FirstOrDefaultAsync(cancellationToken);
            if (candidateId == Guid.Empty) return null;

            var claimId = Guid.CreateVersion7();
            var changeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
            var update = Builders<TaskDocument>.Update.Set(task => task.ReminderClaimedAt, utcNow).Set(task => task.ReminderClaimId, claimId).Set(task => task.UpdatedAt, utcNow).Set(task => task.ChangeVersion, changeVersion);
            var claimedTask = await database.Tasks.FindOneAndUpdateAsync(dueFilter & Builders<TaskDocument>.Filter.Eq(task => task.Id, candidateId), update, new FindOneAndUpdateOptions<TaskDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
            if (claimedTask is null) continue;

            await taskListAccess.NotifyChangedAsync(claimedTask, changeVersion, cancellationToken);
            return (claimedTask, claimId);
        }
    }

    private FilterDefinition<TaskDocument> CreateDueFilter(DateTime utcNow)
    {
        var filterBuilder = Builders<TaskDocument>.Filter;
        var abandonedClaimTime = utcNow - options.ClaimTimeout;
        var isUnclaimed = filterBuilder.Eq(task => task.ReminderClaimedAt, null) | filterBuilder.Lt(task => task.ReminderClaimedAt, abandonedClaimTime);
        return filterBuilder.Lte(task => task.ReminderAt, utcNow) & filterBuilder.Eq(task => task.ReminderSentAt, null) & filterBuilder.Eq(task => task.IsDeleted, false) & filterBuilder.Eq(task => task.IsCompleted, false) & isUnclaimed;
    }

    /// <summary>
    /// Sends the claimed reminder to every push device of the recipient, removes devices whose tokens were rejected, and marks the reminder as sent.
    /// A reminder whose recipient lost access to the task's list is marked as sent without a notification.
    /// When nothing was delivered and a send failed for a transient reason, the claim is kept so the reminder is retried once the claim is abandoned.
    /// </summary>
    private async Task HandleClaimAsync(TaskDocument task, Guid claimId, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        if (task.ReminderAt is not { } reminderAt || utcNow - reminderAt > options.MaximumLateness)
        {
            await MarkSentAsync(task, claimId, cancellationToken);
            return;
        }

        var recipientUserId = task.ReminderSetByUserId ?? task.OwnerUserId;
        if (!await taskListAccess.CanAccessListAsync(recipientUserId, task.ListId, cancellationToken))
        {
            await MarkSentAsync(task, claimId, cancellationToken);
            return;
        }

        var language = await database.Users.Find(user => user.Id == recipientUserId).Project(user => user.Language).FirstOrDefaultAsync(cancellationToken) ?? UserLanguages.English;
        var notification = ReminderNotificationText.Create(task.Title, language, task.Id, task.ListId);
        var isAnyDelivered = false;
        var isAnyFailed = false;
        foreach (var device in await deviceService.GetPushDevicesAsync(recipientUserId, cancellationToken))
        {
            if (device.PushToken is not { } pushToken || !_sendersByPlatform.TryGetValue(device.Platform, out var sender)) continue;

            var result = await sender.SendAsync(pushToken, notification, cancellationToken);
            if (result == PushSendResult.Delivered) isAnyDelivered = true;
            else if (result == PushSendResult.InvalidToken) await RemoveDeviceAsync(device, pushToken, cancellationToken);
            else isAnyFailed = true;
        }

        if (isAnyFailed && !isAnyDelivered)
        {
            logger.LogWarning("The reminder of task {TaskId} could not be sent and will be retried.", task.Id);
            return;
        }

        await MarkSentAsync(task, claimId, cancellationToken);
    }

    private async Task RemoveDeviceAsync(DeviceDocument device, string pushToken, CancellationToken cancellationToken)
    {
        logger.LogInformation("Removing device {DeviceId} because the push service rejected its token.", device.Id);
        await deviceService.RemoveRejectedPushTokenAsync(device.Id, pushToken, cancellationToken);
    }

    /// <summary>Marks the reminder as sent if it still carries this claim; a reminder changed in the meantime stays due under its new time.</summary>
    private async Task MarkSentAsync(TaskDocument task, Guid claimId, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var changeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
        var filter = Builders<TaskDocument>.Filter.Eq(stored => stored.Id, task.Id) & Builders<TaskDocument>.Filter.Eq(stored => stored.ReminderClaimId, claimId);
        var update = Builders<TaskDocument>.Update.Set(stored => stored.ReminderSentAt, utcNow).Set(stored => stored.ReminderClaimedAt, null).Set(stored => stored.ReminderClaimId, null).Set(stored => stored.UpdatedAt, utcNow).Set(stored => stored.ChangeVersion, changeVersion);
        var result = await database.Tasks.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.ModifiedCount == 1) await taskListAccess.NotifyChangedAsync(task, changeVersion, cancellationToken);
    }
}
