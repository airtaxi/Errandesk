namespace Errandesk.Server.Services;

/// <summary>
/// Validates IANA time zone IDs and computes a user's local date.
/// </summary>
public static class UserTimeZones
{
    /// <summary>Whether the ID is an IANA time zone known to the system.</summary>
    public static bool IsValid(string timeZoneId) => !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) && (timeZone.HasIanaId || TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _));

    /// <summary>The calendar date at <paramref name="utcNow"/> in the given time zone, falling back to UTC for unknown IDs.</summary>
    public static DateOnly GetToday(string timeZoneId, DateTimeOffset utcNow)
    {
        var localNow = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) ? TimeZoneInfo.ConvertTime(utcNow, timeZone) : utcNow.ToUniversalTime();
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    /// <summary>The local time in the given time zone of a UTC instant, falling back to UTC for unknown IDs.</summary>
    public static DateTimeOffset ToLocal(DateTime utcValue, string timeZoneId)
    {
        var utcOffsetValue = new DateTimeOffset(DateTime.SpecifyKind(utcValue, DateTimeKind.Utc));
        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) ? TimeZoneInfo.ConvertTime(utcOffsetValue, timeZone) : utcOffsetValue;
    }

    /// <summary>
    /// The UTC instant of a wall-clock time in the given time zone. A time skipped by a daylight saving change moves forward by the skipped amount; an ambiguous time uses its first occurrence.
    /// </summary>
    public static DateTime ToUtc(DateTime localValue, string timeZoneId)
    {
        var unspecifiedValue = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)) return DateTime.SpecifyKind(unspecifiedValue, DateTimeKind.Utc);
        if (timeZone.IsInvalidTime(unspecifiedValue)) unspecifiedValue = unspecifiedValue.AddHours(1);
        if (timeZone.IsAmbiguousTime(unspecifiedValue)) return DateTime.SpecifyKind(unspecifiedValue - timeZone.GetAmbiguousTimeOffsets(unspecifiedValue).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedValue, timeZone);
    }

    /// <summary>Moves a UTC instant by whole days on the wall clock of the given time zone, so the local time of day stays the same across daylight saving changes.</summary>
    public static DateTime AddLocalDays(DateTime utcValue, int days, string timeZoneId) => ToUtc(ToLocal(utcValue, timeZoneId).DateTime.AddDays(days), timeZoneId);
}
