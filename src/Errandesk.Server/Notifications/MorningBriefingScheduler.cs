using System.Globalization;
using Errandesk.Server.Agent;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using MongoDB.Driver;

namespace Errandesk.Server.Notifications;

/// <summary>
/// Writes each user's morning briefing once per local day at the user's chosen time, while an AI model is configured, and sends a push notification to the user's mobile devices.
/// Each briefing is claimed atomically on the user before it is written, so several server instances never write the same briefing twice; claims of failed or abandoned runs are taken over after the claim timeout.
/// </summary>
public sealed partial class MorningBriefingScheduler(ServerSettings settings, ErrandeskDatabase database, MorningBriefingWriter writer, DeviceService deviceService, IEnumerable<IPushNotificationSender> senders, ChangeVersionGenerator changeVersionGenerator, ChangeNotifier changeNotifier, DocumentUpdater documentUpdater, MorningBriefingOptions options, TimeProvider timeProvider, ILogger<MorningBriefingScheduler> logger) : BackgroundService
{
    private readonly Dictionary<string, IPushNotificationSender> _sendersByPlatform = senders.ToDictionary(sender => sender.Platform, StringComparer.Ordinal);

    /// <summary>Writes the briefings that are due now and returns how many were written. Does nothing when no AI model is configured.</summary>
    public async Task<int> WriteDueBriefingsAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsAiAvailable) return 0;

        var utcNow = timeProvider.GetUtcNow();
        var users = await database.Users.Find(user => !user.IsDeleted && user.MorningBriefing!.IsEnabled).ToListAsync(cancellationToken);
        var writtenCount = 0;
        foreach (var user in users)
        {
            if (user.MorningBriefing is not { } briefingSettings || GetDueDate(briefingSettings, user.TimeZone, utcNow) is not { } date) continue;
            if (await ClaimAsync(user.Id, date, cancellationToken) is not { } claimId) continue;
            if (await WriteAndDeliverAsync(user, date, claimId, cancellationToken)) writtenCount++;
        }
        return writtenCount;
    }

    /// <summary>
    /// The user's local date whose briefing is due at <paramref name="utcNow"/>: the chosen time has passed today by at most the maximum lateness and today's briefing was not written yet.
    /// </summary>
    public DateOnly? GetDueDate(MorningBriefingSettingsDocument briefingSettings, string timeZone, DateTimeOffset utcNow)
    {
        if (!TimeOnly.TryParseExact(briefingSettings.Time, InputValidation.BriefingTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;

        var localNow = UserTimeZones.ToLocal(utcNow.UtcDateTime, timeZone).DateTime;
        var today = DateOnly.FromDateTime(localNow);
        if (briefingSettings.LastBriefingDate >= today) return null;

        var lateness = localNow - today.ToDateTime(time);
        return lateness >= TimeSpan.Zero && lateness <= options.MaximumLateness ? today : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(options.PollInterval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try { await WriteDueBriefingsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { LogCheckFailed(logger, exception); }
        }
    }

    /// <summary>Claims the briefing of <paramref name="date"/> unless it was written already or another run holds a claim that is not abandoned.</summary>
    private async Task<Guid?> ClaimAsync(Guid userId, DateOnly date, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var filterBuilder = Builders<UserDocument>.Filter;
        var isNotWritten = filterBuilder.Eq(user => user.MorningBriefing!.LastBriefingDate, null) | filterBuilder.Lt(user => user.MorningBriefing!.LastBriefingDate, date);
        var isUnclaimed = filterBuilder.Eq(user => user.MorningBriefing!.ClaimedAt, null) | filterBuilder.Lt(user => user.MorningBriefing!.ClaimedAt, utcNow - options.ClaimTimeout);
        var filter = filterBuilder.Eq(user => user.Id, userId) & filterBuilder.Eq(user => user.IsDeleted, false) & filterBuilder.Eq(user => user.MorningBriefing!.IsEnabled, true) & isNotWritten & isUnclaimed;

        var claimId = Guid.CreateVersion7();
        var update = Builders<UserDocument>.Update.Set(user => user.MorningBriefing!.ClaimedAt, utcNow).Set(user => user.MorningBriefing!.ClaimId, claimId).Set(user => user.UpdatedAt, utcNow).Set(user => user.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        var result = await database.Users.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1 ? claimId : null;
    }

    /// <summary>
    /// Writes and stores the briefing, marks the day as done and notifies the user's devices. A failed write keeps the claim, so the briefing is tried again once the claim is abandoned.
    /// </summary>
    private async Task<bool> WriteAndDeliverAsync(UserDocument user, DateOnly date, Guid claimId, CancellationToken cancellationToken)
    {
        WrittenMorningBriefing briefing;
        try { briefing = await writer.WriteAsync(user.Id, date, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            LogWriteFailed(logger, exception, user.Id);
            return false;
        }

        await MarkWrittenAsync(user.Id, date, claimId, cancellationToken);
        changeNotifier.NotifyChanged(user.Id, await changeVersionGenerator.GetCurrentAsync(cancellationToken));
        try { await SendNotificationsAsync(user, briefing, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { LogNotificationFailed(logger, exception, user.Id); }
        return true;
    }

    private async Task MarkWrittenAsync(Guid userId, DateOnly date, Guid claimId, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var filter = Builders<UserDocument>.Filter.Eq(user => user.Id, userId) & Builders<UserDocument>.Filter.Eq(user => user.MorningBriefing!.ClaimId, claimId);
        var update = Builders<UserDocument>.Update.Set(user => user.MorningBriefing!.LastBriefingDate, date).Set(user => user.MorningBriefing!.ClaimedAt, null).Set(user => user.MorningBriefing!.ClaimId, null).Set(user => user.UpdatedAt, utcNow).Set(user => user.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.Users.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <summary>Sends the start of the briefing to every mobile device of the user and removes devices whose tokens the push service rejected.</summary>
    private async Task SendNotificationsAsync(UserDocument user, WrittenMorningBriefing briefing, CancellationToken cancellationToken)
    {
        var notification = MorningBriefingNotificationText.Create(user.Language, briefing.Text, briefing.ConversationId);
        foreach (var device in await deviceService.GetPushDevicesAsync(user.Id, cancellationToken))
        {
            if (device.PushToken is not { } pushToken || !_sendersByPlatform.TryGetValue(device.Platform, out var sender)) continue;

            var result = await sender.SendAsync(pushToken, notification, cancellationToken);
            if (result == PushSendResult.InvalidToken) await deviceService.RemoveRejectedPushTokenAsync(device.Id, pushToken, cancellationToken);
            else if (result == PushSendResult.Failed) LogPushFailed(logger, device.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking for due morning briefings failed.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The morning briefing of user {UserId} could not be written and will be tried again.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The morning briefing notifications of user {UserId} could not be sent.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The morning briefing could not be pushed to device {DeviceId}.")]
    private static partial void LogPushFailed(ILogger logger, Guid deviceId);
}
