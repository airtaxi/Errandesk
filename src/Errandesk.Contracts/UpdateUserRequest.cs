using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Changes the signed-in user's profile. Only specified properties change.
/// </summary>
public sealed record UpdateUserRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    /// <summary>IANA time zone, such as <c>Europe/Berlin</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TimeZone { get; init; }

    /// <summary><c>en</c> or <c>ko</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Language { get; init; }

    /// <summary>Whether the assistant writes a morning briefing every day.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsMorningBriefingEnabled { get; init; }

    /// <summary>Local time of the morning briefing as <c>HH:mm</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MorningBriefingTime { get; init; }
}
