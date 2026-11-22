namespace Errandesk.Server.Data;

/// <summary>
/// Names of the MongoDB collections used by the server.
/// </summary>
public static class MongoCollectionNames
{
    public const string Users = "users";
    public const string Invites = "invites";
    public const string RefreshTokens = "refresh_tokens";
    public const string TaskLists = "task_lists";
    public const string Tasks = "tasks";
    public const string ChangeCounters = "change_counters";
    public const string Notes = "notes";
    public const string Conversations = "conversations";
    public const string ConversationMessages = "conversation_messages";
    public const string AgentActions = "agent_actions";
    public const string Devices = "devices";
    public const string IdempotencyKeys = "idempotency_keys";
    public const string ImportJobs = "import_jobs";
    public const string ApiTokens = "api_tokens";
}
