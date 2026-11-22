using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Contracts;
using Microsoft.Data.Sqlite;

namespace Errandesk.Client.Sync;

/// <summary>
/// The SQLite database of one user on one server: lists, tasks and notes as JSON payloads with indexed columns,
/// the synchronization state, the outbox of changes waiting for the server and the items to read again from the server.
/// </summary>
/// <remarks>Members are synchronous and serialized, so the store can be used from any thread.</remarks>
public sealed class LocalStore : IDisposable
{
    private const int SchemaVersion = 1;
    private const string VersionKey = "version";
    private const string LastSyncedAtKey = "lastSyncedAt";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly string[] s_schemaStatements =
    [
        "CREATE TABLE IF NOT EXISTS lists (id TEXT PRIMARY KEY, sort_order REAL NOT NULL, change_version INTEGER NOT NULL, payload TEXT NOT NULL)",
        "CREATE TABLE IF NOT EXISTS tasks (id TEXT PRIMARY KEY, list_id TEXT NOT NULL, is_important INTEGER NOT NULL, is_completed INTEGER NOT NULL, due_date TEXT NULL, my_day_date TEXT NULL, reminder_at TEXT NULL, change_version INTEGER NOT NULL, payload TEXT NOT NULL)",
        "CREATE INDEX IF NOT EXISTS tasks_list_id ON tasks (list_id)",
        "CREATE INDEX IF NOT EXISTS tasks_my_day_date ON tasks (my_day_date)",
        "CREATE INDEX IF NOT EXISTS tasks_due_date ON tasks (due_date)",
        "CREATE INDEX IF NOT EXISTS tasks_is_important ON tasks (is_important)",
        "CREATE INDEX IF NOT EXISTS tasks_reminder_at ON tasks (reminder_at)",
        "CREATE TABLE IF NOT EXISTS notes (id TEXT PRIMARY KEY, title TEXT NOT NULL, excerpt TEXT NOT NULL, tags TEXT NOT NULL, is_pinned INTEGER NOT NULL, updated_at TEXT NOT NULL, change_version INTEGER NOT NULL, payload TEXT NOT NULL)",
        "CREATE TABLE IF NOT EXISTS note_links (note_id TEXT NOT NULL, task_id TEXT NOT NULL, PRIMARY KEY (note_id, task_id))",
        "CREATE INDEX IF NOT EXISTS note_links_task_id ON note_links (task_id)",
        "CREATE TABLE IF NOT EXISTS sync_state (key TEXT PRIMARY KEY, value TEXT NOT NULL)",
        "CREATE TABLE IF NOT EXISTS outbox (sequence INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL UNIQUE, entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, method TEXT NOT NULL, path TEXT NOT NULL, body TEXT NULL, attempts INTEGER NOT NULL DEFAULT 0, last_error TEXT NULL, created_at TEXT NOT NULL)",
        "CREATE INDEX IF NOT EXISTS outbox_entity ON outbox (entity_type, entity_id)",
        "CREATE TABLE IF NOT EXISTS refresh_queue (entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, PRIMARY KEY (entity_type, entity_id))",
    ];

    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private SqliteTransaction? _transaction;

    private LocalStore(SqliteConnection connection) => _connection = connection;

    /// <summary>
    /// Opens the database at <paramref name="databasePath"/>, creating the file and its tables when they do not exist.
    /// </summary>
    public static LocalStore Open(string databasePath)
    {
        var directoryPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directoryPath)) Directory.CreateDirectory(directoryPath);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            var store = new LocalStore(connection);
            store.CreateSchema();
            return store;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Deletes the database file at <paramref name="databasePath"/> and its journal files. The store must be closed.
    /// </summary>
    public static void Delete(string databasePath)
    {
        foreach (var path in (string[])[databasePath, $"{databasePath}-wal", $"{databasePath}-shm", $"{databasePath}-journal"])
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The change version of the server up to which changes were applied; 0 before the first synchronization.
    /// </summary>
    public long Version
    {
        get => long.TryParse(GetState(VersionKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : 0;
        set => SetState(VersionKey, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// When a synchronization last completed.
    /// </summary>
    public DateTimeOffset? LastSyncedAt
    {
        get => DateTimeOffset.TryParse(GetState(LastSyncedAtKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
        set => SetState(LastSyncedAtKey, value?.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs <paramref name="action"/> in one transaction, so its changes are stored together or not at all. Calls may be nested.
    /// </summary>
    public void RunInTransaction(Action action)
    {
        lock (_gate)
        {
            if (_transaction is not null)
            {
                action();
                return;
            }

            _transaction = _connection.BeginTransaction();
            try
            {
                action();
                _transaction.Commit();
            }
            catch
            {
                _transaction.Rollback();
                throw;
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }
    }

    public IReadOnlyList<TaskListResponse> GetLists() => QueryPayloads("SELECT payload FROM lists ORDER BY sort_order, id", JsonContext.TaskListResponse);

    public TaskListResponse? GetList(Guid id) => QueryPayloads("SELECT payload FROM lists WHERE id = $id", JsonContext.TaskListResponse, ("$id", ToText(id))).FirstOrDefault();

    public void UpsertList(TaskListResponse list) => Execute("INSERT INTO lists (id, sort_order, change_version, payload) VALUES ($id, $sortOrder, $changeVersion, $payload) ON CONFLICT (id) DO UPDATE SET sort_order = excluded.sort_order, change_version = excluded.change_version, payload = excluded.payload", ("$id", ToText(list.Id)), ("$sortOrder", list.SortOrder), ("$changeVersion", list.ChangeVersion), ("$payload", JsonSerializer.Serialize(list, JsonContext.TaskListResponse)));

    /// <returns><see langword="true"/> when the list was stored.</returns>
    public bool DeleteList(Guid id) => Execute("DELETE FROM lists WHERE id = $id", ("$id", ToText(id))) > 0;

    public IReadOnlyList<TaskItemResponse> GetAllTasks() => QueryTasks("1 = 1");

    public IReadOnlyList<TaskItemResponse> GetTasksInList(Guid listId) => QueryTasks("list_id = $listId", ("$listId", ToText(listId)));

    public IReadOnlyList<TaskItemResponse> GetTasksInMyDay(DateOnly today) => QueryTasks("my_day_date = $today", ("$today", ToText(today)));

    public IReadOnlyList<TaskItemResponse> GetImportantTasks() => QueryTasks("is_important = 1");

    public IReadOnlyList<TaskItemResponse> GetPlannedTasks() => QueryTasks("due_date IS NOT NULL");

    public IReadOnlyList<TaskItemResponse> GetOpenTasksWithReminders() => QueryTasks("reminder_at IS NOT NULL AND is_completed = 0");

    public TaskItemResponse? GetTask(Guid id) => QueryTasks("id = $id", ("$id", ToText(id))).FirstOrDefault();

    public IReadOnlyList<Guid> GetTaskIdsInList(Guid listId) => QueryIds("SELECT id FROM tasks WHERE list_id = $listId", ("$listId", ToText(listId)));

    public void UpsertTask(TaskItemResponse task)
    {
        var storedTask = task with { LinkedNotes = [] };
        Execute("INSERT INTO tasks (id, list_id, is_important, is_completed, due_date, my_day_date, reminder_at, change_version, payload) VALUES ($id, $listId, $isImportant, $isCompleted, $dueDate, $myDayDate, $reminderAt, $changeVersion, $payload) ON CONFLICT (id) DO UPDATE SET list_id = excluded.list_id, is_important = excluded.is_important, is_completed = excluded.is_completed, due_date = excluded.due_date, my_day_date = excluded.my_day_date, reminder_at = excluded.reminder_at, change_version = excluded.change_version, payload = excluded.payload", ("$id", ToText(task.Id)), ("$listId", ToText(task.ListId)), ("$isImportant", task.IsImportant), ("$isCompleted", task.IsCompleted), ("$dueDate", task.DueDate is { } dueDate ? ToText(dueDate) : null), ("$myDayDate", task.MyDayDate is { } myDayDate ? ToText(myDayDate) : null), ("$reminderAt", task.ReminderAt is { } reminderAt ? ToText(reminderAt) : null), ("$changeVersion", task.ChangeVersion), ("$payload", JsonSerializer.Serialize(storedTask, JsonContext.TaskItemResponse)));
    }

    /// <returns><see langword="true"/> when the task was stored.</returns>
    public bool DeleteTask(Guid id) => Execute("DELETE FROM tasks WHERE id = $id", ("$id", ToText(id))) > 0;

    public IReadOnlyList<NoteResponse> GetNotes() => QueryPayloads("SELECT payload FROM notes ORDER BY is_pinned DESC, updated_at DESC, id DESC", JsonContext.NoteResponse);

    public NoteResponse? GetNote(Guid id) => QueryPayloads("SELECT payload FROM notes WHERE id = $id", JsonContext.NoteResponse, ("$id", ToText(id))).FirstOrDefault();

    public void UpsertNote(NoteResponse note)
    {
        RunInTransaction(() =>
        {
            Execute("INSERT INTO notes (id, title, excerpt, tags, is_pinned, updated_at, change_version, payload) VALUES ($id, $title, $excerpt, $tags, $isPinned, $updatedAt, $changeVersion, $payload) ON CONFLICT (id) DO UPDATE SET title = excluded.title, excerpt = excluded.excerpt, tags = excluded.tags, is_pinned = excluded.is_pinned, updated_at = excluded.updated_at, change_version = excluded.change_version, payload = excluded.payload", ("$id", ToText(note.Id)), ("$title", note.Title), ("$excerpt", LocalEditRules.CreateExcerpt(note.Content)), ("$tags", JsonSerializer.Serialize(note.Tags, JsonContext.IReadOnlyListString)), ("$isPinned", note.IsPinned), ("$updatedAt", ToText(note.UpdatedAt)), ("$changeVersion", note.ChangeVersion), ("$payload", JsonSerializer.Serialize(note, JsonContext.NoteResponse)));
            Execute("DELETE FROM note_links WHERE note_id = $id", ("$id", ToText(note.Id)));
            foreach (var taskId in note.LinkedTaskIds.Distinct()) Execute("INSERT INTO note_links (note_id, task_id) VALUES ($noteId, $taskId)", ("$noteId", ToText(note.Id)), ("$taskId", ToText(taskId)));
        });
    }

    /// <returns><see langword="true"/> when the note was stored.</returns>
    public bool DeleteNote(Guid id)
    {
        var isDeleted = false;
        RunInTransaction(() =>
        {
            Execute("DELETE FROM note_links WHERE note_id = $id", ("$id", ToText(id)));
            isDeleted = Execute("DELETE FROM notes WHERE id = $id", ("$id", ToText(id))) > 0;
        });
        return isDeleted;
    }

    /// <summary>
    /// The tasks a stored note is linked to.
    /// </summary>
    public IReadOnlyList<Guid> GetLinkedTaskIds(Guid noteId) => QueryIds("SELECT task_id FROM note_links WHERE note_id = $noteId", ("$noteId", ToText(noteId)));

    /// <summary>
    /// Summaries of the notes linked to each of <paramref name="taskIds"/>, pinned notes first, then the most recently changed.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<NoteSummaryResponse>> GetLinkedNoteSummaries(IReadOnlyCollection<Guid> taskIds)
    {
        var requestedTaskIds = taskIds as IReadOnlySet<Guid> ?? taskIds.ToHashSet();
        Dictionary<Guid, List<NoteSummaryResponse>> summaries = [];
        if (requestedTaskIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<NoteSummaryResponse>>();

        lock (_gate)
        {
            using var command = CreateCommand("SELECT note_links.task_id, notes.id, notes.title, notes.excerpt, notes.tags, notes.is_pinned, notes.updated_at FROM note_links JOIN notes ON notes.id = note_links.note_id ORDER BY notes.is_pinned DESC, notes.updated_at DESC, notes.id DESC");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var taskId = Guid.Parse(reader.GetString(0));
                if (!requestedTaskIds.Contains(taskId)) continue;

                var tags = JsonSerializer.Deserialize(reader.GetString(4), JsonContext.IReadOnlyListString) ?? [];
                var summary = new NoteSummaryResponse(Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), tags, reader.GetBoolean(5), DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
                if (!summaries.TryGetValue(taskId, out var taskSummaries))
                {
                    taskSummaries = [];
                    summaries[taskId] = taskSummaries;
                }

                taskSummaries.Add(summary);
            }
        }

        return summaries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<NoteSummaryResponse>)pair.Value);
    }

    /// <summary>
    /// The change version of a stored list, task or note, or <see langword="null"/> when it is not stored.
    /// </summary>
    public long? GetChangeVersion(string entityType, Guid id)
    {
        lock (_gate)
        {
            using var command = CreateCommand($"SELECT change_version FROM {GetTableName(entityType)} WHERE id = $id", ("$id", ToText(id)));
            return command.ExecuteScalar() is long changeVersion ? changeVersion : null;
        }
    }

    /// <summary>
    /// The IDs of every stored list, task or note.
    /// </summary>
    public IReadOnlyList<Guid> GetIds(string entityType) => QueryIds($"SELECT id FROM {GetTableName(entityType)}");

    /// <summary>
    /// Adds a change to the end of the outbox.
    /// </summary>
    public OutboxOperation Enqueue(string entityType, Guid entityId, string method, string path, string? body, DateTimeOffset createdAt)
    {
        var operationId = Guid.CreateVersion7();
        lock (_gate)
        {
            using var command = CreateCommand("INSERT INTO outbox (operation_id, entity_type, entity_id, method, path, body, created_at) VALUES ($operationId, $entityType, $entityId, $method, $path, $body, $createdAt) RETURNING sequence", ("$operationId", ToText(operationId)), ("$entityType", entityType), ("$entityId", ToText(entityId)), ("$method", method), ("$path", path), ("$body", body), ("$createdAt", ToText(createdAt)));
            var sequence = (long)command.ExecuteScalar()!;
            return new OutboxOperation(sequence, operationId, entityType, entityId, method, path, body, 0, null, createdAt);
        }
    }

    /// <summary>
    /// The oldest operation of the outbox, or <see langword="null"/> when it is empty.
    /// </summary>
    public OutboxOperation? PeekOperation() => QueryOperations("SELECT sequence, operation_id, entity_type, entity_id, method, path, body, attempts, last_error, created_at FROM outbox ORDER BY sequence LIMIT 1").FirstOrDefault();

    /// <summary>
    /// Every operation of the outbox in the order it will be sent.
    /// </summary>
    public IReadOnlyList<OutboxOperation> GetOperations() => QueryOperations("SELECT sequence, operation_id, entity_type, entity_id, method, path, body, attempts, last_error, created_at FROM outbox ORDER BY sequence");

    public void RemoveOperation(long sequence) => Execute("DELETE FROM outbox WHERE sequence = $sequence", ("$sequence", sequence));

    /// <summary>
    /// Removes every outbox operation of one item.
    /// </summary>
    public void RemoveOperations(string entityType, Guid entityId) => Execute("DELETE FROM outbox WHERE entity_type = $entityType AND entity_id = $entityId", ("$entityType", entityType), ("$entityId", ToText(entityId)));

    /// <summary>
    /// Counts a failed send of an operation and keeps why it failed.
    /// </summary>
    public void RecordFailure(long sequence, string error) => Execute("UPDATE outbox SET attempts = attempts + 1, last_error = $error WHERE sequence = $sequence", ("$sequence", sequence), ("$error", error));

    public int PendingOperationCount
    {
        get
        {
            lock (_gate)
            {
                using var command = CreateCommand("SELECT COUNT(*) FROM outbox");
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// The IDs of the items of <paramref name="entityType"/> that have operations in the outbox.
    /// </summary>
    public IReadOnlySet<Guid> GetPendingEntityIds(string entityType) => QueryIds("SELECT DISTINCT entity_id FROM outbox WHERE entity_type = $entityType", ("$entityType", entityType)).ToHashSet();

    public bool HasPendingOperations(string entityType, Guid entityId)
    {
        lock (_gate)
        {
            using var command = CreateCommand("SELECT EXISTS (SELECT 1 FROM outbox WHERE entity_type = $entityType AND entity_id = $entityId)", ("$entityType", entityType), ("$entityId", ToText(entityId)));
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }
    }

    /// <summary>
    /// The lists whose deletion waits in the outbox.
    /// </summary>
    public IReadOnlySet<Guid> GetListIdsPendingDeletion() => QueryIds("SELECT DISTINCT entity_id FROM outbox WHERE entity_type = $entityType AND method = $method", ("$entityType", EntityTypes.List), ("$method", HttpMethod.Delete.Method)).ToHashSet();

    public void QueueRefresh(string entityType, Guid entityId) => Execute("INSERT OR IGNORE INTO refresh_queue (entity_type, entity_id) VALUES ($entityType, $entityId)", ("$entityType", entityType), ("$entityId", ToText(entityId)));

    public void RemoveRefresh(string entityType, Guid entityId) => Execute("DELETE FROM refresh_queue WHERE entity_type = $entityType AND entity_id = $entityId", ("$entityType", entityType), ("$entityId", ToText(entityId)));

    public IReadOnlyList<RefreshItem> GetRefreshQueue()
    {
        lock (_gate)
        {
            using var command = CreateCommand("SELECT entity_type, entity_id FROM refresh_queue ORDER BY entity_type, entity_id");
            using var reader = command.ExecuteReader();
            List<RefreshItem> items = [];
            while (reader.Read()) items.Add(new RefreshItem(reader.GetString(0), Guid.Parse(reader.GetString(1))));
            return items;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private void CreateSchema()
    {
        lock (_gate)
        {
            using (var journalCommand = CreateCommand("PRAGMA journal_mode = WAL")) journalCommand.ExecuteScalar();
            using var versionCommand = CreateCommand("PRAGMA user_version");
            var storedSchemaVersion = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (storedSchemaVersion >= SchemaVersion) return;

            RunInTransaction(() =>
            {
                foreach (var statement in s_schemaStatements) Execute(statement);
                Execute($"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
            });
        }
    }

    private IReadOnlyList<TaskItemResponse> QueryTasks(string condition, params (string Name, object? Value)[] parameters) => QueryPayloads($"SELECT payload FROM tasks WHERE {condition}", JsonContext.TaskItemResponse, parameters);

    private List<T> QueryPayloads<T>(string commandText, JsonTypeInfo<T> typeInfo, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = CreateCommand(commandText, parameters);
            using var reader = command.ExecuteReader();
            List<T> items = [];
            while (reader.Read())
            {
                if (JsonSerializer.Deserialize(reader.GetString(0), typeInfo) is { } item)
                {
                    items.Add(item);
                }
            }

            return items;
        }
    }

    private List<Guid> QueryIds(string commandText, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = CreateCommand(commandText, parameters);
            using var reader = command.ExecuteReader();
            List<Guid> ids = [];
            while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
            return ids;
        }
    }

    private List<OutboxOperation> QueryOperations(string commandText)
    {
        lock (_gate)
        {
            using var command = CreateCommand(commandText);
            using var reader = command.ExecuteReader();
            List<OutboxOperation> operations = [];
            while (reader.Read()) operations.Add(new OutboxOperation(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8), DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            return operations;
        }
    }

    private string? GetState(string key)
    {
        lock (_gate)
        {
            using var command = CreateCommand("SELECT value FROM sync_state WHERE key = $key", ("$key", key));
            return command.ExecuteScalar() as string;
        }
    }

    private void SetState(string key, string? value)
    {
        if (value is null) Execute("DELETE FROM sync_state WHERE key = $key", ("$key", key));
        else Execute("INSERT INTO sync_state (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value", ("$key", key), ("$value", value));
    }

    private int Execute(string commandText, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = CreateCommand(commandText, parameters);
            return command.ExecuteNonQuery();
        }
    }

    private SqliteCommand CreateCommand(string commandText, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = _transaction;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private static string GetTableName(string entityType) => entityType switch
    {
        EntityTypes.List => "lists",
        EntityTypes.Task => "tasks",
        EntityTypes.Note => "notes",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type."),
    };

    private static string ToText(Guid value) => value.ToString("D");

    private static string ToText(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
