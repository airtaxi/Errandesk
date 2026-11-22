using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Changes a note. Only specified properties change; <c>null</c> clears the title, the content or the tags.
/// Links to tasks change through the link and unlink endpoints.
/// </summary>
public sealed record UpdateNoteRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Content { get; init; }

    /// <summary>Replaces all tags, normalized as on create.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Tags { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsPinned { get; init; }

    /// <summary>When set, the change applies only if the note still has this change version; otherwise the server answers 409 with the current note.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedChangeVersion { get; init; }
}
