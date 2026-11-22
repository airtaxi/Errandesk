using MongoDB.Bson;

namespace Errandesk.Server.Data;

/// <summary>
/// One document affected by an agent action, with its stored form before and after the change.
/// </summary>
public sealed class AgentActionEntityDocument
{
    public string EntityType { get; set; } = "";

    public Guid EntityId { get; set; }

    /// <summary>The document before the change, or <see langword="null"/> when the change created it.</summary>
    public BsonDocument? Before { get; set; }

    /// <summary>The document after the change.</summary>
    public BsonDocument After { get; set; } = [];
}
