using System.Text.Json.Nodes;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// A Microsoft To Do task served by <see cref="FakeMicrosoftTodo"/>: its Graph JSON plus the JSON of its checklist items.
/// </summary>
public sealed class FakeTodoTask(string id, JsonObject? properties = null)
{
    public string Id { get; } = id;

    /// <summary>The task as Graph returns it, including <c>id</c>.</summary>
    public JsonObject Json { get; } = CreateJson(id, properties);

    public List<JsonObject> ChecklistItems { get; } = [];

    public static FakeTodoTask Create(string id, string title)
    {
        var task = new FakeTodoTask(id);
        task.Json["title"] = title;
        return task;
    }

    private static JsonObject CreateJson(string id, JsonObject? properties)
    {
        var json = properties ?? new JsonObject { ["title"] = id, ["importance"] = "normal", ["status"] = "notStarted", ["isReminderOn"] = false };
        json["id"] = id;
        return json;
    }
}
