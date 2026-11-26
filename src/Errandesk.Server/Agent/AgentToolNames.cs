namespace Errandesk.Server.Agent;

/// <summary>
/// Names of the assistant tools as the model sees them.
/// </summary>
public static class AgentToolNames
{
    public const string ListTaskLists = "list_task_lists";
    public const string CreateTaskList = "create_task_list";
    public const string SearchTasks = "search_tasks";
    public const string GetMyDay = "get_my_day";
    public const string GetOverdueTasks = "get_overdue_tasks";
    public const string CreateTask = "create_task";
    public const string UpdateTask = "update_task";
    public const string CompleteTask = "complete_task";
    public const string AddStep = "add_step";
    public const string CompleteStep = "complete_step";
    public const string AddToMyDay = "add_to_my_day";
    public const string SearchNotes = "search_notes";
    public const string ReadNote = "read_note";
    public const string CreateNote = "create_note";
    public const string UpdateNote = "update_note";
    public const string EditNoteContent = "edit_note_content";
    public const string LinkNoteToTask = "link_note_to_task";
    public const string GetCurrentTime = "get_current_time";
    public const string ReadUrl = "read_url";
    public const string SetReminder = "set_reminder";
    public const string SetRecurrence = "set_recurrence";
    public const string DeleteTask = "delete_task";
    public const string DeleteTaskList = "delete_task_list";
    public const string DeleteNote = "delete_note";
}
