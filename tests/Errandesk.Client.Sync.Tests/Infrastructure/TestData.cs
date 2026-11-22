using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests.Infrastructure;

/// <summary>
/// Builds server responses for the tests.
/// </summary>
public static class TestData
{
    public static readonly DateTimeOffset CreatedAt = new(2031, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public static TaskListResponse List(Guid id, string name, long changeVersion, bool isDefault = false, double sortOrder = 0) => new(id, name, sortOrder, isDefault, CreatedAt, CreatedAt, changeVersion);

    public static TaskItemResponse Task(Guid id, Guid listId, string title, long changeVersion, double sortOrder = 0, bool isImportant = false, DateOnly? dueDate = null) => new(id, listId, title, "", isImportant, false, null, dueDate, null, false, sortOrder, [], CreatedAt, CreatedAt, changeVersion, []);

    public static NoteResponse Note(Guid id, string title, long changeVersion, IReadOnlyList<Guid>? linkedTaskIds = null, IReadOnlyList<string>? tags = null) => new(id, title, "", tags ?? [], linkedTaskIds ?? [], false, CreatedAt, CreatedAt, changeVersion);

    public static TombstoneResponse Tombstone(Guid id, long changeVersion) => new(id, changeVersion, CreatedAt);

    public static ChangesResponse Changes(long currentVersion, IReadOnlyList<TaskListResponse>? lists = null, IReadOnlyList<TaskItemResponse>? tasks = null, IReadOnlyList<NoteResponse>? notes = null, IReadOnlyList<TombstoneResponse>? deletedLists = null, IReadOnlyList<TombstoneResponse>? deletedTasks = null, IReadOnlyList<TombstoneResponse>? deletedNotes = null, bool resetRequired = false) => new(currentVersion, lists ?? [], tasks ?? [], notes ?? [], deletedLists ?? [], deletedTasks ?? [], deletedNotes ?? [], resetRequired);
}
