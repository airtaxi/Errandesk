using System.Globalization;
using Errandesk.Contracts;
using Errandesk.Server.Data;

namespace Errandesk.Server.Agent;

/// <summary>
/// Builds the request that asks the model for a morning briefing.
/// </summary>
public static class MorningBriefingPrompt
{
    /// <summary>Name of My Day in the Korean app.</summary>
    public const string KoreanMyDayName = "오늘 할 일";

    /// <summary>Name of the default list in the Korean app.</summary>
    public const string KoreanDefaultListName = "작업";

    /// <summary>The request text: what to write, in the user's language, followed by the day's data as JSON.</summary>
    public static string CreateRequest(UserDocument user, DateOnly date, string dataJson)
    {
        var isKorean = user.Language == UserLanguages.Korean;
        var language = isKorean ? "Korean" : "English";
        var appTerms = isKorean ? $" The app calls My Day \"{KoreanMyDayName}\" and the default list \"{KoreanDefaultListName}\"; use these names." : "";
        return string.Create(CultureInfo.InvariantCulture, $"""
            Write my morning briefing for {date:yyyy-MM-dd} ({date.DayOfWeek}) in {language}.{appTerms}

            Use only the data below and never add tasks, notes or details that are not in it. Start with one short greeting line. Then write short sections, in this order and only for the parts that have entries: overdue tasks, tasks due today, My Day, and notes changed in the last day. Start each section with its name in bold on a line of its own, not with a Markdown heading, followed by a bulleted list. A task can appear in several parts of the data; mention each task only once, in the first section that applies. Give reminder times as the local times in the data. End with one sentence that suggests what to start with. When there is nothing at all, say in one or two sentences that the day is clear. Keep the briefing under 200 words, do not ask questions, and use plain Markdown without emoji or decorative symbols.

            Data as JSON:
            {dataJson}
            """);
    }
}
