using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Changes a step of a task. Only specified properties change.
/// </summary>
public sealed record UpdateTaskStepRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsCompleted { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> SortOrder { get; init; }

    /// <summary>When set, the change applies only if the task still has this change version; otherwise the server answers 409 with the current task.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedChangeVersion { get; init; }
}
