namespace Errandesk.Client.Models;

/// <summary>
/// A request to show a task, for example after the user tapped its reminder notification.
/// </summary>
/// <param name="TaskId">The task to show.</param>
/// <param name="ListId">The list that contained the task when the notification was created, used when the task cannot be read.</param>
public sealed record TaskNavigationRequest(Guid TaskId, Guid? ListId)
{
    /// <summary>
    /// Reads a request from the task and list IDs that notifications carry as text.
    /// </summary>
    public static bool TryParse(string? taskIdText, string? listIdText, out TaskNavigationRequest request)
    {
        if (!Guid.TryParse(taskIdText, out var taskId) || taskId == Guid.Empty)
        {
            request = null!;
            return false;
        }

        request = new TaskNavigationRequest(taskId, Guid.TryParse(listIdText, out var listId) ? listId : null);
        return true;
    }
}
