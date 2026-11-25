using System.Globalization;
using Errandesk.Contracts;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Describes assistant tools and approval requests in the interface language.
/// </summary>
public static class AssistantTextHelper
{
    private static readonly Dictionary<string, string> s_toolResourceNames = new(StringComparer.Ordinal)
    {
        ["list_task_lists"] = "AssistantToolListTaskListsText",
        ["create_task_list"] = "AssistantToolCreateTaskListText",
        ["search_tasks"] = "AssistantToolSearchTasksText",
        ["get_my_day"] = "AssistantToolGetMyDayText",
        ["get_overdue_tasks"] = "AssistantToolGetOverdueTasksText",
        ["create_task"] = "AssistantToolCreateTaskText",
        ["update_task"] = "AssistantToolUpdateTaskText",
        ["complete_task"] = "AssistantToolCompleteTaskText",
        ["add_step"] = "AssistantToolAddStepText",
        ["complete_step"] = "AssistantToolCompleteStepText",
        ["add_to_my_day"] = "AssistantToolAddToMyDayText",
        ["search_notes"] = "AssistantToolSearchNotesText",
        ["read_note"] = "AssistantToolReadNoteText",
        ["create_note"] = "AssistantToolCreateNoteText",
        ["update_note"] = "AssistantToolUpdateNoteText",
        ["link_note_to_task"] = "AssistantToolLinkNoteToTaskText",
        ["get_current_time"] = "AssistantToolGetCurrentTimeText",
        ["delete_task"] = "AssistantToolDeleteTaskText",
        ["delete_task_list"] = "AssistantToolDeleteTaskListText",
        ["delete_note"] = "AssistantToolDeleteNoteText",
        ["set_reminder"] = "AssistantToolSetReminderText",
        ["set_recurrence"] = "AssistantToolSetRecurrenceText",
    };

    /// <summary>
    /// What a tool does, such as "Created a task". Unknown tools are shown by name.
    /// </summary>
    public static string GetToolText(string toolName) => s_toolResourceNames.TryGetValue(toolName, out var resourceName) && LocalizedStrings.Get(resourceName) is { Length: > 0 } text ? text : toolName;

    /// <summary>
    /// The question of an approval card, such as "Delete the task "Buy milk"?".
    /// </summary>
    public static string GetApprovalPrompt(string toolName, string entityType, string entityTitle)
    {
        var resourceName = entityType switch
        {
            EntityTypes.List => "ApprovalDeleteListFormat",
            EntityTypes.Note => "ApprovalDeleteNoteFormat",
            _ => "ApprovalDeleteTaskFormat",
        };

        if (entityTitle.Length == 0) return string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ApprovalUnknownItemFormat"), GetToolText(toolName));
        return string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(resourceName), entityTitle);
    }
}
