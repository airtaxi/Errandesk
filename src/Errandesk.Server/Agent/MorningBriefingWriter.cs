using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Agent;

/// <summary>
/// Writes a user's morning briefing: collects the overdue, due-today and My Day tasks and the notes changed in the last day, asks the model for a short summary without tools,
/// and stores the request and the briefing in the user's briefing conversation.
/// </summary>
public sealed class MorningBriefingWriter(IServiceProvider serviceProvider, UserService userService, TaskListService taskListService, TaskService taskService, NoteService noteService, MorningBriefingService briefingService, TimeProvider timeProvider)
{
    public const int MaximumOutputTokens = 1024;
    public const int MaximumTasksPerSection = 30;
    public const int MaximumNoteCount = 10;

    /// <summary>How far back notes count as recently changed.</summary>
    public static readonly TimeSpan RecentNoteWindow = TimeSpan.FromDays(1);

    private const string LocalDateTimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The tool JSON settings, keeping non-ASCII text such as Korean titles readable instead of escaping it.</summary>
    private static readonly JsonSerializerOptions s_dataSerializerOptions = new(AgentJsonSerializerOptions.Default) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Writes and stores the briefing of <paramref name="userId"/> for the local date <paramref name="date"/>.</summary>
    /// <exception cref="InvalidOperationException">No AI model is configured, or the model returned no text.</exception>
    /// <exception cref="ResourceConflictException">The assistant is answering in the briefing conversation.</exception>
    public async Task<WrittenMorningBriefing> WriteAsync(Guid userId, DateOnly date, CancellationToken cancellationToken)
    {
        var chatClient = serviceProvider.GetService<IChatClient>() ?? throw new InvalidOperationException("No AI model is configured.");
        var user = await userService.GetAsync(userId, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var data = await CollectDataAsync(user, date, utcNow, cancellationToken);
        var requestMessage = new ChatMessage(ChatRole.User, MorningBriefingPrompt.CreateRequest(user, date, JsonSerializer.Serialize(data, s_dataSerializerOptions)));

        var response = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.System, AgentSystemPrompt.Create(user, utcNow)), requestMessage], new ChatOptions { MaxOutputTokens = MaximumOutputTokens }, cancellationToken);
        var text = response.Text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("The model returned an empty briefing.");

        var conversation = await briefingService.EnsureConversationAsync(userId, cancellationToken);
        var runId = Guid.CreateVersion7();
        ConversationMessageDocument[] messages =
        [
            new() { Id = Guid.CreateVersion7(), RunId = runId, Role = ConversationMessageDocument.UserRole, Text = "", ContentsJson = ChatMessageSerialization.SerializeContents(requestMessage.Contents), BriefingDate = date },
            new() { Id = Guid.CreateVersion7(), RunId = runId, Role = ConversationMessageDocument.AssistantRole, Text = text, ContentsJson = ChatMessageSerialization.SerializeContents([new TextContent(text)]), BriefingDate = date },
        ];
        await briefingService.AppendAsync(userId, conversation.Id, runId, messages, cancellationToken);
        return new WrittenMorningBriefing(conversation.Id, runId, text);
    }

    private async Task<MorningBriefingData> CollectDataAsync(UserDocument user, DateOnly date, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var defaultListName = user.Language == UserLanguages.Korean ? MorningBriefingPrompt.KoreanDefaultListName : null;
        var listNames = (await taskListService.ListAsync(user.Id, cancellationToken)).ToDictionary(list => list.Id, list => list.IsDefault && defaultListName is not null ? defaultListName : list.Name);
        var overdueTasks = await taskService.GetOverdueAsync(user.Id, cancellationToken);
        var dueTodayTasks = (await taskService.QueryAsync(user.Id, TaskViews.Planned, false, cancellationToken)).Where(task => task.DueDate == date);
        var myDayTasks = await taskService.QueryAsync(user.Id, TaskViews.MyDay, false, cancellationToken);
        var notes = await noteService.GetRecentlyChangedAsync(user.Id, (utcNow - RecentNoteWindow).UtcDateTime, MaximumNoteCount, cancellationToken);

        List<MorningBriefingTaskData> ToData(IEnumerable<TaskItemResponse> tasks) =>
        [.. tasks.Take(MaximumTasksPerSection).Select(task => CreateTaskData(task, listNames, user.TimeZone))];
        var noteData = notes.Select(note => new MorningBriefingNoteData(note.Title, note.Tags, note.Excerpt, FormatLocal(note.UpdatedAt, user.TimeZone))).ToList();
        return new MorningBriefingData(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), user.TimeZone, ToData(overdueTasks), ToData(dueTodayTasks), ToData(myDayTasks), noteData);
    }

    private static MorningBriefingTaskData CreateTaskData(TaskItemResponse task, Dictionary<Guid, string> listNames, string timeZone)
    {
        var steps = task.Steps.Count == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{task.Steps.Count(step => step.IsCompleted)} of {task.Steps.Count} done");
        var reminder = task.ReminderAt is { } reminderAt ? FormatLocal(reminderAt, timeZone) : null;
        return new MorningBriefingTaskData(task.Title, listNames.GetValueOrDefault(task.ListId, ""), task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), task.IsImportant, steps, reminder);
    }

    private static string FormatLocal(DateTimeOffset value, string timeZone) => UserTimeZones.ToLocal(value.UtcDateTime, timeZone).ToString(LocalDateTimeFormat, CultureInfo.InvariantCulture);
}
