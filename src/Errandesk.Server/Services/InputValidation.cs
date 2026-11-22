using System.Globalization;
using System.Text.RegularExpressions;
using Errandesk.Contracts;

namespace Errandesk.Server.Services;

/// <summary>
/// Validates and normalizes request values, throwing <see cref="RequestValidationException"/> for invalid input.
/// </summary>
public static partial class InputValidation
{
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 512;
    public const int MaximumDisplayNameLength = 100;
    public const int MaximumDeviceNameLength = 100;
    public const int MaximumListNameLength = 256;
    public const int MaximumTaskTitleLength = 500;
    public const int MaximumStepTitleLength = 500;
    public const int MaximumTaskBodyLength = 100_000;
    public const int MaximumStepCount = 200;
    public const int MaximumNoteTitleLength = 500;
    public const int MaximumNoteContentLength = 1_000_000;
    public const int MaximumTagLength = 50;
    public const int MaximumTagCount = 30;
    public const int MaximumLinkedTaskCount = 100;
    public const int MaximumSearchQueryLength = 200;
    public const int MaximumPushTokenLength = 4096;
    public const int MaximumAppVersionLength = 50;
    public const int MinimumReminderYear = 2000;
    public const int MaximumReminderYear = 9000;
    public const string DefaultDeviceName = "Unknown device";
    public const string BriefingTimeFormat = "HH:mm";

    /// <summary>Returns the trimmed text, or throws when it is empty or longer than <paramref name="maximumLength"/>.</summary>
    public static string RequireText(string? value, string fieldName, int maximumLength)
    {
        var trimmedValue = value?.Trim();
        if (string.IsNullOrEmpty(trimmedValue)) throw new RequestValidationException($"The {fieldName} must not be empty.");
        if (trimmedValue.Length > maximumLength) throw new RequestValidationException($"The {fieldName} must be at most {maximumLength} characters long.");
        return trimmedValue;
    }

    /// <summary>Returns the text unchanged, or an empty string for <see langword="null"/>, and throws when it is too long.</summary>
    public static string OptionalText(string? value, string fieldName, int maximumLength)
    {
        var text = value ?? "";
        if (text.Length > maximumLength) throw new RequestValidationException($"The {fieldName} must be at most {maximumLength} characters long.");
        return text;
    }

    public static string UserName(string? value)
    {
        var userName = value?.Trim() ?? "";
        if (!UserNameRegex().IsMatch(userName)) throw new RequestValidationException("The user name must be 3 to 32 letters, digits, dots, hyphens or underscores.");
        return userName;
    }

    public static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();

    public static string Password(string? value, string fieldName = "password")
    {
        if (value is null || value.Length < MinimumPasswordLength) throw new RequestValidationException($"The {fieldName} must be at least {MinimumPasswordLength} characters long.");
        if (value.Length > MaximumPasswordLength) throw new RequestValidationException($"The {fieldName} must be at most {MaximumPasswordLength} characters long.");
        return value;
    }

    public static string TimeZone(string? value)
    {
        var timeZoneId = value?.Trim() ?? "";
        if (!UserTimeZones.IsValid(timeZoneId)) throw new RequestValidationException("The time zone must be an IANA time zone ID such as Europe/Berlin.");
        return timeZoneId;
    }

    public static string Language(string? value)
    {
        var language = value?.Trim().ToLowerInvariant() ?? "";
        if (!UserLanguages.All.Contains(language)) throw new RequestValidationException($"The language must be one of: {string.Join(", ", UserLanguages.All)}.");
        return language;
    }

    public static string DeviceName(string? value)
    {
        var deviceName = value?.Trim();
        if (string.IsNullOrEmpty(deviceName)) return DefaultDeviceName;
        return deviceName.Length > MaximumDeviceNameLength ? deviceName[..MaximumDeviceNameLength] : deviceName;
    }

    /// <summary>Returns the tag in lower case without surrounding white space and leading <c>#</c> characters, or throws when it is empty, too long, or contains white space or commas.</summary>
    public static string Tag(string? value)
    {
        var tag = (value ?? "").Trim().TrimStart('#').ToLowerInvariant();
        if (tag.Length == 0) throw new RequestValidationException("A tag must not be empty.");
        if (tag.Length > MaximumTagLength) throw new RequestValidationException($"A tag must be at most {MaximumTagLength} characters long.");
        if (tag.Any(character => char.IsWhiteSpace(character) || character == ',')) throw new RequestValidationException("A tag must not contain spaces or commas.");
        return tag;
    }

    /// <summary>Normalizes every tag with <see cref="Tag"/> and removes duplicates, keeping the first occurrence.</summary>
    public static List<string> Tags(IReadOnlyList<string>? values)
    {
        if (values is null) return [];
        var tags = values.Select(Tag).Distinct(StringComparer.Ordinal).ToList();
        if (tags.Count > MaximumTagCount) throw new RequestValidationException($"A note can have at most {MaximumTagCount} tags.");
        return tags;
    }

    /// <summary>Returns the trimmed search text, or <see langword="null"/> when it is empty, and throws when it is too long.</summary>
    public static string? SearchQuery(string? value)
    {
        var query = value?.Trim();
        if (string.IsNullOrEmpty(query)) return null;
        if (query.Length > MaximumSearchQueryLength) throw new RequestValidationException($"The search query must be at most {MaximumSearchQueryLength} characters long.");
        return query;
    }

    public static double SortOrder(double value) => double.IsFinite(value) ? value : throw new RequestValidationException("The sort order must be a finite number.");

    /// <summary>The reminder time in UTC, truncated to milliseconds, or throws when it is outside the supported years.</summary>
    public static DateTime ReminderTime(DateTimeOffset value)
    {
        var utcValue = value.UtcDateTime;
        if (utcValue.Year < MinimumReminderYear || utcValue.Year > MaximumReminderYear) throw new RequestValidationException($"The reminder time must be between the years {MinimumReminderYear} and {MaximumReminderYear}.");
        return utcValue.AddTicks(-(utcValue.Ticks % TimeSpan.TicksPerMillisecond));
    }

    /// <summary>Returns the platform in lower case, or throws when it is not one of <see cref="DevicePlatforms.All"/>.</summary>
    /// <summary>Returns a local time of day as <c>HH:mm</c>, or throws when the value is not a 24-hour time in that form.</summary>
    public static string BriefingTime(string? value)
    {
        var text = value?.Trim() ?? "";
        var isValid = TimeOnly.TryParseExact(text, BriefingTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) && time.ToString(BriefingTimeFormat, CultureInfo.InvariantCulture) == text;
        return isValid ? text : throw new RequestValidationException("The briefing time must be a time of day as HH:mm, such as 07:30.");
    }

    public static string DevicePlatform(string? value)
    {
        var platform = value?.Trim().ToLowerInvariant() ?? "";
        if (!DevicePlatforms.All.Contains(platform)) throw new RequestValidationException($"The platform must be one of: {string.Join(", ", DevicePlatforms.All)}.");
        return platform;
    }

    /// <summary>Returns the trimmed push token, or <see langword="null"/> when it is empty. Only mobile platforms take a token; iOS tokens are hexadecimal.</summary>
    public static string? PushToken(string? value, string platform)
    {
        var pushToken = value?.Trim();
        if (string.IsNullOrEmpty(pushToken)) return null;
        if (!DevicePlatforms.Mobile.Contains(platform)) throw new RequestValidationException("Only Android and iOS devices can register a push token.");
        if (pushToken.Length > MaximumPushTokenLength) throw new RequestValidationException($"The push token must be at most {MaximumPushTokenLength} characters long.");
        if (platform == DevicePlatforms.Ios && !pushToken.All(char.IsAsciiHexDigit)) throw new RequestValidationException("An iOS push token must be hexadecimal.");
        return platform == DevicePlatforms.Ios ? pushToken.ToLowerInvariant() : pushToken;
    }

    public static string AppVersion(string? value)
    {
        var appVersion = value?.Trim() ?? "";
        return appVersion.Length > MaximumAppVersionLength ? appVersion[..MaximumAppVersionLength] : appVersion;
    }

    public static Guid ClientId(Guid? value) => value switch
    {
        null => Guid.CreateVersion7(),
        { } identifier when identifier == Guid.Empty => throw new RequestValidationException("The ID must not be empty."),
        { } identifier => identifier,
    };

    [GeneratedRegex(@"^[\p{L}\p{N}._-]{3,32}$")]
    private static partial Regex UserNameRegex();
}
