namespace Errandesk.Server.Agent;

/// <summary>
/// Everything the model may use to write a morning briefing.
/// </summary>
/// <param name="Date">The user's local date as <c>yyyy-MM-dd</c>.</param>
/// <param name="TimeZone">IANA time zone of the user.</param>
/// <param name="OverdueTasks">Open tasks whose due date has passed, earliest first.</param>
/// <param name="DueTodayTasks">Open tasks due today.</param>
/// <param name="MyDayTasks">Open tasks in today's My Day.</param>
/// <param name="RecentlyChangedNotes">Notes changed in the last day, most recent first.</param>
public sealed record MorningBriefingData(string Date, string TimeZone, IReadOnlyList<MorningBriefingTaskData> OverdueTasks, IReadOnlyList<MorningBriefingTaskData> DueTodayTasks, IReadOnlyList<MorningBriefingTaskData> MyDayTasks, IReadOnlyList<MorningBriefingNoteData> RecentlyChangedNotes);
