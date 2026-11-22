using System.Text;
using Errandesk.Contracts;

namespace Errandesk.Server.Notifications;

/// <summary>
/// The text and data of morning briefing notifications.
/// </summary>
public static class MorningBriefingNotificationText
{
    public const string ConversationIdKey = "briefingConversationId";
    public const int MaximumBodyLength = 160;

    /// <summary>The notification title in the user's language, English for unknown languages.</summary>
    public static string GetTitle(string language) => language == UserLanguages.Korean ? "아침 브리핑" : "Morning briefing";

    public static PushNotification Create(string language, string briefingText, Guid conversationId) => new(GetTitle(language), CreateBody(briefingText), new Dictionary<string, string> { [ConversationIdKey] = conversationId.ToString() });

    /// <summary>
    /// The start of the briefing as plain text: heading, list and emphasis markers are removed, lines are joined with spaces, and the text is cut at <see cref="MaximumBodyLength"/> characters.
    /// </summary>
    public static string CreateBody(string briefingText)
    {
        var bodyBuilder = new StringBuilder();
        foreach (var rawLine in briefingText.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('#', '>', '-', '*', '+', ' ').Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal).Trim();
            if (line.Length == 0) continue;
            if (bodyBuilder.Length > 0) bodyBuilder.Append(' ');
            bodyBuilder.Append(line);
            if (bodyBuilder.Length >= MaximumBodyLength) break;
        }

        var body = bodyBuilder.ToString();
        return body.Length <= MaximumBodyLength ? body : $"{body[..(MaximumBodyLength - 1)].TrimEnd()}…";
    }
}
