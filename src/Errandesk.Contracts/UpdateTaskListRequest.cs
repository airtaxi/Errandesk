using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Changes a task list. Only specified properties change.
/// </summary>
public sealed record UpdateTaskListRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> SortOrder { get; init; }

    /// <summary>When set, the change applies only if the list still has this change version; otherwise the server answers 409 with the current list.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedChangeVersion { get; init; }
}
