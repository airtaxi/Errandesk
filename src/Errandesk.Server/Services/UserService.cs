using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Reads and changes user profiles and computes each user's local date.
/// </summary>
public sealed class UserService(ErrandeskDatabase database, DocumentUpdater documentUpdater, TimeProvider timeProvider)
{
    private const string ResourceName = "user";

    public async Task<UserDocument> GetAsync(Guid userId, CancellationToken cancellationToken) => await database.Users.Find(VisibleFilter(userId)).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    public async Task<UserResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken) => (await GetAsync(userId, cancellationToken)).ToResponse();

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName.IsSpecified ? InputValidation.RequireText(request.DisplayName.Value, "display name", InputValidation.MaximumDisplayNameLength) : null;
        var timeZone = request.TimeZone.IsSpecified ? InputValidation.TimeZone(request.TimeZone.Value) : null;
        var language = request.Language.IsSpecified ? InputValidation.Language(request.Language.Value) : null;
        var briefingTime = request.MorningBriefingTime.IsSpecified ? InputValidation.BriefingTime(request.MorningBriefingTime.Value) : null;
        bool? isBriefingEnabled = request.IsMorningBriefingEnabled.IsSpecified ? request.IsMorningBriefingEnabled.Value : null;

        var user = await documentUpdater.UpdateAsync(database.Users, VisibleFilter(userId), ResourceName, null, current => current.ToResponse(), current =>
        {
            if (displayName is not null) current.DisplayName = displayName;
            if (timeZone is not null) current.TimeZone = timeZone;
            if (language is not null) current.Language = language;
            if (briefingTime is null && isBriefingEnabled is null) return;

            current.MorningBriefing ??= new MorningBriefingSettingsDocument { Time = MorningBriefingSettings.DefaultTime };
            if (briefingTime is not null) current.MorningBriefing.Time = briefingTime;
            if (isBriefingEnabled is { } isEnabled) current.MorningBriefing.IsEnabled = isEnabled;
        }, cancellationToken);
        return user.ToResponse();
    }

    /// <summary>Every user, oldest first.</summary>
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await database.Users.Find(user => !user.IsDeleted).SortBy(user => user.CreatedAt).ToListAsync(cancellationToken);
        return [.. users.Select(user => user.ToResponse())];
    }

    /// <summary>The current date in the user's time zone.</summary>
    public async Task<DateOnly> GetTodayAsync(Guid userId, CancellationToken cancellationToken)
    {
        var timeZone = await database.Users.Find(VisibleFilter(userId)).Project(user => user.TimeZone).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);
        return UserTimeZones.GetToday(timeZone, timeProvider.GetUtcNow());
    }

    private static FilterDefinition<UserDocument> VisibleFilter(Guid userId) => Builders<UserDocument>.Filter.Where(user => user.Id == userId && !user.IsDeleted);
}
