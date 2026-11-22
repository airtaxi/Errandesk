namespace Errandesk.Server.Agent;

/// <summary>
/// The result of <c>get_current_time</c>.
/// </summary>
/// <param name="LocalDateTime">Local date and time in ISO 8601 form without an offset.</param>
/// <param name="Date">Local date as <c>yyyy-MM-dd</c>.</param>
/// <param name="DayOfWeek">English name of the local weekday.</param>
/// <param name="TimeZone">IANA time zone of the user.</param>
/// <param name="UtcOffset">Offset of the local time from UTC, such as <c>+09:00</c>.</param>
public sealed record AgentCurrentTime(string LocalDateTime, string Date, string DayOfWeek, string TimeZone, string UtcOffset);
