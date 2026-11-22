namespace Errandesk.Server.Data;

/// <summary>
/// A named monotonic counter in the <c>change_counters</c> collection.
/// </summary>
public sealed class ChangeCounterDocument
{
    public string Id { get; set; } = "";

    public long Value { get; set; }
}
