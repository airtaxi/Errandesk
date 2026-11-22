namespace Errandesk.Server.Import;

/// <summary>
/// A wall-clock time with its time zone (<c>dateTimeTimeZone</c>), such as <c>2020-08-25T04:00:00.0000000</c> in <c>UTC</c> or <c>Pacific Standard Time</c>.
/// </summary>
public sealed record GraphDateTimeTimeZone(string? DateTime, string? TimeZone);
