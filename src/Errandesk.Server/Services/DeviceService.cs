using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Registers and removes the client devices of a user and provides the push targets for reminders.
/// </summary>
public sealed class DeviceService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater)
{
    private const string ResourceName = "device";

    /// <summary>The user's devices, most recently seen first.</summary>
    public async Task<IReadOnlyList<DeviceResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await database.Devices.Find(device => device.OwnerUserId == userId).SortByDescending(device => device.LastSeenAt).ToListAsync(cancellationToken);
        return [.. devices.Select(device => device.ToResponse())];
    }

    /// <summary>
    /// Creates or updates the device with the client's ID for the user, taking it over when another user registered the same installation before.
    /// A push token is kept on one device only: other devices with the same token are removed.
    /// </summary>
    public async Task<CreateResult<DeviceResponse>> RegisterAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty) throw new RequestValidationException("The ID must not be empty.");
        var platform = InputValidation.DevicePlatform(request.Platform);
        var pushToken = InputValidation.PushToken(request.PushToken, platform);
        var deviceName = InputValidation.DeviceName(request.DeviceName);
        var appVersion = InputValidation.AppVersion(request.AppVersion);

        if (pushToken is not null) await database.Devices.DeleteManyAsync(device => device.PushToken == pushToken && device.Id != request.Id, cancellationToken);

        var utcNow = documentUpdater.GetUtcNow();
        var changeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
        var update = Builders<DeviceDocument>.Update
            .Set(device => device.OwnerUserId, userId)
            .Set(device => device.Platform, platform)
            .Set(device => device.DeviceName, deviceName)
            .Set(device => device.PushToken, pushToken)
            .Set(device => device.AppVersion, appVersion)
            .Set(device => device.LastSeenAt, utcNow)
            .Set(device => device.UpdatedAt, utcNow)
            .Set(device => device.ChangeVersion, changeVersion)
            .Set(device => device.IsDeleted, false)
            .Set(device => device.DeletedAt, null)
            .Set(device => device.SchemaVersion, DocumentBase.CurrentSchemaVersion)
            .SetOnInsert(device => device.CreatedAt, utcNow);
        var options = new FindOneAndUpdateOptions<DeviceDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.Before };
        var previousDevice = await database.Devices.FindOneAndUpdateAsync(Builders<DeviceDocument>.Filter.Eq(device => device.Id, request.Id), update, options, cancellationToken);
        var response = new DeviceResponse(request.Id, platform, deviceName, appVersion, pushToken is not null, ResponseMappings.ToUtcOffset(utcNow), ResponseMappings.ToUtcOffset(previousDevice?.CreatedAt ?? utcNow));
        return new CreateResult<DeviceResponse>(response, previousDevice is null);
    }

    /// <summary>Removes one of the user's devices so it receives no more push notifications.</summary>
    public async Task DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var result = await database.Devices.DeleteOneAsync(device => device.Id == deviceId && device.OwnerUserId == userId, cancellationToken);
        if (result.DeletedCount == 0) throw new ResourceNotFoundException(ResourceName);
    }

    /// <summary>The user's Android and iOS devices that registered a push token.</summary>
    public async Task<IReadOnlyList<DeviceDocument>> GetPushDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<DeviceDocument>.Filter;
        var filter = filterBuilder.Eq(device => device.OwnerUserId, userId) & filterBuilder.Ne(device => device.PushToken, null) & filterBuilder.In(device => device.Platform, DevicePlatforms.Mobile);
        return await database.Devices.Find(filter).ToListAsync(cancellationToken);
    }

    /// <summary>Removes the device when it still has the push token that the push service rejected.</summary>
    public async Task RemoveRejectedPushTokenAsync(Guid deviceId, string pushToken, CancellationToken cancellationToken) => await database.Devices.DeleteOneAsync(device => device.Id == deviceId && device.PushToken == pushToken, cancellationToken);
}
