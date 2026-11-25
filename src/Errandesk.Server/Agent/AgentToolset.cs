using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Microsoft.Extensions.AI;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Errandesk.Server.Agent;

/// <summary>
/// The tools the assistant uses to read and change the tasks and notes of one user. Every tool calls the same services as the API controllers;
/// tools that change data record an agent action that can be undone.
/// </summary>
public sealed class AgentToolset(TaskListService taskListService, TaskService taskService, NoteService noteService, UserService userService, AgentActionService agentActionService, TimeProvider timeProvider, AgentRunContext context)
{
    public const int MaximumSearchResultCount = 50;

    private static readonly HashSet<string> s_approvalRequiredToolNames = [AgentToolNames.DeleteTask, AgentToolNames.DeleteTaskList, AgentToolNames.DeleteNote];
    private static readonly HashSet<string> s_readOnlyToolNames = [AgentToolNames.ListTaskLists, AgentToolNames.SearchTasks, AgentToolNames.GetMyDay, AgentToolNames.GetOverdueTasks, AgentToolNames.SearchNotes, AgentToolNames.ReadNote, AgentToolNames.GetCurrentTime];

    private Guid UserId => context.UserId;

    /// <summary>Whether the tool deletes data and runs only after the user approved the call.</summary>
    public static bool RequiresApproval(string toolName) => s_approvalRequiredToolNames.Contains(toolName);

    /// <summary>Whether the tool only reads data.</summary>
    public static bool IsReadOnly(string toolName) => s_readOnlyToolNames.Contains(toolName);

    /// <summary>Creates the function tools for the model. The delete tools require the user's approval.</summary>
    public IList<AITool> CreateTools()
    {
        var serializerOptions = AgentJsonSerializerOptions.Default;
        AIFunction Create(Delegate method, string name, string description) => AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name, Description = description, SerializerOptions = serializerOptions });

        return
        [
            Create(ListTaskListsAsync, AgentToolNames.ListTaskLists, "Lists the user's task lists with their IDs, including lists other users shared with them; owner is the user who owns the list and shares it, members are the people it is shared with. The list with isDefault true is the user's default list, shown as \"Tasks\", when the user is its owner."),
            Create(CreateTaskListAsync, AgentToolNames.CreateTaskList, "Creates a task list."),
            Create(SearchTasksAsync, AgentToolNames.SearchTasks, $"Finds tasks whose title, note or steps contain every word of the query. Without a query, returns all tasks of the list, or of all lists. Returns at most {MaximumSearchResultCount} tasks."),
            Create(GetMyDayAsync, AgentToolNames.GetMyDay, "Returns the open tasks in today's My Day."),
            Create(GetOverdueTasksAsync, AgentToolNames.GetOverdueTasks, "Returns the open tasks whose due date has passed, earliest first."),
            Create(CreateTaskAsync, AgentToolNames.CreateTask, "Creates a task. Without a list ID it goes into the default list."),
            Create(UpdateTaskAsync, AgentToolNames.UpdateTask, "Changes a task. Only the given values change."),
            Create(CompleteTaskAsync, AgentToolNames.CompleteTask, "Marks a task as completed, or as not completed."),
            Create(AddStepAsync, AgentToolNames.AddStep, "Adds a step at the end of a task's steps."),
            Create(CompleteStepAsync, AgentToolNames.CompleteStep, "Marks a step of a task as completed, or as not completed. The step IDs are in the steps of the task data."),
            Create(AddToMyDayAsync, AgentToolNames.AddToMyDay, "Adds a task to today's My Day, or removes it."),
            Create(SearchNotesAsync, AgentToolNames.SearchNotes, $"Finds notes by words in the title, content or tags, by tag, or by linked task. Returns summaries of at most {MaximumSearchResultCount} notes; use read_note for the full content."),
            Create(ReadNoteAsync, AgentToolNames.ReadNote, "Returns a note with its full Markdown content."),
            Create(CreateNoteAsync, AgentToolNames.CreateNote, "Creates a Markdown note."),
            Create(UpdateNoteAsync, AgentToolNames.UpdateNote, "Changes a note. Only the given values change; tags replace all existing tags."),
            Create(LinkNoteToTaskAsync, AgentToolNames.LinkNoteToTask, "Links a note to a task."),
            Create(GetCurrentTimeAsync, AgentToolNames.GetCurrentTime, "Returns the current local date, time and weekday of the user."),
            Create(SetReminderAsync, AgentToolNames.SetReminder, "Sets the reminder of a task to a local date and time in the user's time zone, or removes it. The user gets a notification at that time."),
            Create(SetRecurrenceAsync, AgentToolNames.SetRecurrence, "Makes a task repeat, changes how it repeats, or stops it from repeating. A repeating task without a due date becomes due today, or on the first chosen weekday from today. Completing a repeating task creates its next occurrence, with the reminder moved by the same number of days."),
            new ApprovalRequiredAIFunction(Create(DeleteTaskAsync, AgentToolNames.DeleteTask, "Deletes a task. The user must approve this.")),
            new ApprovalRequiredAIFunction(Create(DeleteTaskListAsync, AgentToolNames.DeleteTaskList, "Deletes a task list together with all its tasks. The default list cannot be deleted. The user must approve this.")),
            new ApprovalRequiredAIFunction(Create(DeleteNoteAsync, AgentToolNames.DeleteNote, "Deletes a note. The user must approve this.")),
        ];
    }

    public Task<object> ListTaskListsAsync(CancellationToken cancellationToken) => RunAsync(AgentToolNames.ListTaskLists, async () => new AgentToolOutcome(await taskListService.ListAsync(UserId, cancellationToken)));

    public Task<object> CreateTaskListAsync([Description("Name of the new list.")] string name, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.CreateTaskList, () => ChangeAsync(AgentToolNames.CreateTaskList, EntityTypes.List, null, async () => (await taskListService.CreateAsync(UserId, new CreateTaskListRequest(name), cancellationToken)).Resource, list => list.Id, cancellationToken));

    public Task<object> SearchTasksAsync([Description("Words to search for. Leave empty to list tasks.")] string? query = null, [Description("Only search this list.")] Guid? listId = null, [Description("Also return completed tasks.")] bool includeCompleted = false, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.SearchTasks, async () => new AgentToolOutcome((await taskService.SearchAsync(UserId, query, listId, includeCompleted, cancellationToken)).Take(MaximumSearchResultCount).ToList()));

    public Task<object> GetMyDayAsync(CancellationToken cancellationToken) => RunAsync(AgentToolNames.GetMyDay, async () => new AgentToolOutcome(await taskService.QueryAsync(UserId, TaskViews.MyDay, false, cancellationToken)));

    public Task<object> GetOverdueTasksAsync(CancellationToken cancellationToken) => RunAsync(AgentToolNames.GetOverdueTasks, async () => new AgentToolOutcome(await taskService.GetOverdueAsync(UserId, cancellationToken)));

    public Task<object> CreateTaskAsync([Description("Title of the task.")] string title, [Description("List for the task. Omit for the default list.")] Guid? listId = null, [Description("Due date as yyyy-MM-dd.")] DateOnly? dueDate = null, [Description("Mark the task as important.")] bool isImportant = false, [Description("Add the task to today's My Day.")] bool addToMyDay = false, [Description("Plain-text note of the task.")] string? note = null, [Description("Titles of steps to add.")] string[]? steps = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateTaskRequest(title, ListId: listId, Body: note, IsImportant: isImportant, DueDate: dueDate, IsInMyDay: addToMyDay, Steps: steps is null ? null : [.. steps.Select(step => new CreateTaskStepRequest(step))]);
        return RunAsync(AgentToolNames.CreateTask, () => ChangeAsync(AgentToolNames.CreateTask, EntityTypes.Task, null, async () => (await taskService.CreateAsync(UserId, request, cancellationToken)).Resource, task => task.Id, cancellationToken));
    }

    public Task<object> UpdateTaskAsync([Description("ID of the task.")] Guid taskId, [Description("New title.")] string? title = null, [Description("New plain-text note. An empty text removes the note.")] string? note = null, [Description("Whether the task is important.")] bool? isImportant = null, [Description("New due date as yyyy-MM-dd.")] DateOnly? dueDate = null, [Description("Remove the due date.")] bool removeDueDate = false, [Description("Move the task to this list.")] Guid? listId = null, CancellationToken cancellationToken = default)
    {
        var request = new UpdateTaskRequest
        {
            Title = title is null ? default : new Optional<string>(title),
            Body = note is null ? default : new Optional<string?>(note),
            IsImportant = isImportant is { } important ? new Optional<bool>(important) : default,
            DueDate = removeDueDate ? new Optional<DateOnly?>(null) : dueDate is { } date ? new Optional<DateOnly?>(date) : default,
            ListId = listId is { } targetListId ? new Optional<Guid>(targetListId) : default,
        };
        return RunAsync(AgentToolNames.UpdateTask, () => ChangeAsync(AgentToolNames.UpdateTask, EntityTypes.Task, taskId, () => taskService.UpdateAsync(UserId, taskId, request, cancellationToken), task => task.Id, cancellationToken));
    }

    /// <summary>Completes or reopens a task. When completing a repeating task creates its next occurrence, the action records that occurrence too, so undo removes it.</summary>
    public Task<object> CompleteTaskAsync([Description("ID of the task.")] Guid taskId, [Description("False marks the task as not completed.")] bool completed = true, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.CompleteTask, async () =>
    {
        var before = await agentActionService.CaptureAsync(EntityTypes.Task, UserId, taskId, cancellationToken);
        var task = await taskService.UpdateAsync(UserId, taskId, new UpdateTaskRequest { IsCompleted = completed }, cancellationToken);
        List<AgentActionEntityDocument> entities = [await CreateEntityAsync(EntityTypes.Task, taskId, before, cancellationToken)];
        var previousNextOccurrenceId = before is null ? null : BsonSerializer.Deserialize<TaskDocument>(before).NextOccurrenceId;
        if (task.NextOccurrenceId is { } nextOccurrenceId && previousNextOccurrenceId is null && await agentActionService.CaptureAsync(EntityTypes.Task, UserId, nextOccurrenceId, cancellationToken) is { } nextOccurrence)
        {
            entities.Add(new AgentActionEntityDocument { EntityType = EntityTypes.Task, EntityId = nextOccurrenceId, Before = null, After = nextOccurrence });
        }

        var agentActionId = await agentActionService.RecordAsync(UserId, context.ConversationId, AgentToolNames.CompleteTask, GetCurrentArgumentsJson(), entities, cancellationToken);
        return new AgentToolOutcome(task, agentActionId);
    });

    public Task<object> SetReminderAsync([Description("ID of the task.")] Guid taskId, [Description("Local date and time of the reminder in the user's time zone, as yyyy-MM-ddTHH:mm.")] string? reminderTime = null, [Description("Remove the reminder instead of setting one.")] bool remove = false, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.SetReminder, async () =>
    {
        var user = await userService.GetAsync(UserId, cancellationToken);
        DateTimeOffset? reminderAt = remove ? null : new DateTimeOffset(UserTimeZones.ToUtc(ParseLocalDateTime(reminderTime), user.TimeZone));
        var outcome = await ChangeAsync(AgentToolNames.SetReminder, EntityTypes.Task, taskId, () => taskService.UpdateAsync(UserId, taskId, new UpdateTaskRequest { ReminderAt = reminderAt }, cancellationToken), task => task.Id, cancellationToken);
        var task = (TaskItemResponse)outcome.Result;
        var reminderLocalTime = task.ReminderAt is { } taskReminderAt ? UserTimeZones.ToLocal(taskReminderAt.UtcDateTime, user.TimeZone).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) : null;
        return outcome with { Result = new AgentReminderResult(task.Id, task.Title, reminderLocalTime, user.TimeZone) };
    });

    public Task<object> SetRecurrenceAsync([Description("ID of the task.")] Guid taskId, [Description("One of: daily, weekdays (Monday to Friday), weekly, monthly, yearly, or none to stop repeating.")] string frequency, [Description("Repeat every this many days, weeks, months or years.")] int interval = 1, [Description("Days for a weekly repeat, such as monday and thursday. Omit to repeat on the weekday of the due date.")] string[]? daysOfWeek = null, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.SetRecurrence, () =>
    {
        var recurrence = CreateRecurrence(frequency, interval, daysOfWeek);
        return ChangeAsync(AgentToolNames.SetRecurrence, EntityTypes.Task, taskId, () => taskService.UpdateAsync(UserId, taskId, new UpdateTaskRequest { Recurrence = recurrence }, cancellationToken), task => task.Id, cancellationToken);
    });

    public Task<object> AddStepAsync([Description("ID of the task.")] Guid taskId, [Description("Title of the step.")] string title, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.AddStep, () => ChangeAsync(AgentToolNames.AddStep, EntityTypes.Task, taskId, () => taskService.AddStepAsync(UserId, taskId, new CreateTaskStepRequest(title), cancellationToken), task => task.Id, cancellationToken));

    public Task<object> CompleteStepAsync([Description("ID of the task.")] Guid taskId, [Description("ID of the step.")] Guid stepId, [Description("False marks the step as not completed.")] bool completed = true, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.CompleteStep, () => ChangeAsync(AgentToolNames.CompleteStep, EntityTypes.Task, taskId, () => taskService.UpdateStepAsync(UserId, taskId, stepId, new UpdateTaskStepRequest { IsCompleted = completed }, cancellationToken), task => task.Id, cancellationToken));

    public Task<object> AddToMyDayAsync([Description("ID of the task.")] Guid taskId, [Description("False removes the task from My Day.")] bool inMyDay = true, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.AddToMyDay, () => ChangeAsync(AgentToolNames.AddToMyDay, EntityTypes.Task, taskId, () => taskService.UpdateAsync(UserId, taskId, new UpdateTaskRequest { IsInMyDay = inMyDay }, cancellationToken), task => task.Id, cancellationToken));

    public Task<object> SearchNotesAsync([Description("Words to search for. Leave empty to list notes.")] string? query = null, [Description("Only notes with this tag.")] string? tag = null, [Description("Only notes linked to this task.")] Guid? linkedTaskId = null, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.SearchNotes, async () =>
    {
        var notes = await noteService.QueryAsync(UserId, query, tag, linkedTaskId, cancellationToken);
        return new AgentToolOutcome(notes.Take(MaximumSearchResultCount).Select(note => new NoteSummaryResponse(note.Id, note.Title, ResponseMappings.CreateExcerpt(note.Content), note.Tags, note.IsPinned, note.UpdatedAt)).ToList());
    });

    public Task<object> ReadNoteAsync([Description("ID of the note.")] Guid noteId, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.ReadNote, async () => new AgentToolOutcome(await noteService.GetAsync(UserId, noteId, cancellationToken)));

    public Task<object> CreateNoteAsync([Description("Title of the note.")] string title, [Description("Markdown content.")] string? content = null, [Description("Tags without the # sign.")] string[]? tags = null, [Description("IDs of tasks to link the note to.")] Guid[]? linkedTaskIds = null, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.CreateNote, () => ChangeAsync(AgentToolNames.CreateNote, EntityTypes.Note, null, async () => (await noteService.CreateAsync(UserId, new CreateNoteRequest(title, content, Tags: tags, LinkedTaskIds: linkedTaskIds), cancellationToken)).Resource, note => note.Id, cancellationToken));

    public Task<object> UpdateNoteAsync([Description("ID of the note.")] Guid noteId, [Description("New title.")] string? title = null, [Description("New Markdown content, replacing the old content.")] string? content = null, [Description("New tags, replacing all existing tags.")] string[]? tags = null, [Description("Whether the note is pinned.")] bool? isPinned = null, CancellationToken cancellationToken = default)
    {
        var request = new UpdateNoteRequest
        {
            Title = title is null ? default : new Optional<string?>(title),
            Content = content is null ? default : new Optional<string?>(content),
            Tags = tags is null ? default : new Optional<IReadOnlyList<string>?>(tags),
            IsPinned = isPinned is { } pinned ? new Optional<bool>(pinned) : default,
        };
        return RunAsync(AgentToolNames.UpdateNote, () => ChangeAsync(AgentToolNames.UpdateNote, EntityTypes.Note, noteId, () => noteService.UpdateAsync(UserId, noteId, request, cancellationToken), note => note.Id, cancellationToken));
    }

    public Task<object> LinkNoteToTaskAsync([Description("ID of the note.")] Guid noteId, [Description("ID of the task.")] Guid taskId, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.LinkNoteToTask, () => ChangeAsync(AgentToolNames.LinkNoteToTask, EntityTypes.Note, noteId, () => noteService.LinkTaskAsync(UserId, noteId, taskId, cancellationToken), note => note.Id, cancellationToken));

    public Task<object> GetCurrentTimeAsync(CancellationToken cancellationToken) => RunAsync(AgentToolNames.GetCurrentTime, async () =>
    {
        var user = await userService.GetAsync(UserId, cancellationToken);
        return new AgentToolOutcome(CreateCurrentTime(user.TimeZone, timeProvider.GetUtcNow()));
    });

    public Task<object> DeleteTaskAsync([Description("ID of the task.")] Guid taskId, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.DeleteTask, async () =>
    {
        var task = await taskService.GetAsync(UserId, taskId, cancellationToken);
        var outcome = await ChangeAsync(AgentToolNames.DeleteTask, EntityTypes.Task, taskId, async () =>
        {
            await taskService.DeleteAsync(UserId, taskId, cancellationToken);
            return taskId;
        }, deletedId => deletedId, cancellationToken);
        return outcome with { Result = new AgentDeletionResult(EntityTypes.Task, taskId, task.Title) };
    });

    public Task<object> DeleteTaskListAsync([Description("ID of the list.")] Guid listId, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.DeleteTaskList, async () =>
    {
        var list = await taskListService.GetAsync(UserId, listId, cancellationToken);
        var listBefore = await agentActionService.CaptureAsync(EntityTypes.List, UserId, listId, cancellationToken);
        var tasksBefore = await agentActionService.CaptureTasksInListAsync(UserId, listId, cancellationToken);
        await taskListService.DeleteAsync(UserId, listId, cancellationToken);

        List<AgentActionEntityDocument> entities = [await CreateEntityAsync(EntityTypes.List, listId, listBefore, cancellationToken)];
        foreach (var taskBefore in tasksBefore) entities.Add(await CreateEntityAsync(EntityTypes.Task, taskBefore.Key, taskBefore.Value, cancellationToken));
        var agentActionId = await agentActionService.RecordAsync(UserId, context.ConversationId, AgentToolNames.DeleteTaskList, GetCurrentArgumentsJson(), entities, cancellationToken);
        return new AgentToolOutcome(new AgentDeletionResult(EntityTypes.List, listId, list.Name), agentActionId);
    });

    public Task<object> DeleteNoteAsync([Description("ID of the note.")] Guid noteId, CancellationToken cancellationToken = default) => RunAsync(AgentToolNames.DeleteNote, async () =>
    {
        var note = await noteService.GetAsync(UserId, noteId, cancellationToken);
        var outcome = await ChangeAsync(AgentToolNames.DeleteNote, EntityTypes.Note, noteId, async () =>
        {
            await noteService.DeleteAsync(UserId, noteId, cancellationToken);
            return noteId;
        }, deletedId => deletedId, cancellationToken);
        return outcome with { Result = new AgentDeletionResult(EntityTypes.Note, noteId, note.Title) };
    });

    /// <summary>The item a destructive tool call would affect, looked up from the call's arguments.</summary>
    public async Task<AgentApprovalTarget> DescribeApprovalTargetAsync(string toolName, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        var (entityType, argumentName) = toolName switch
        {
            AgentToolNames.DeleteTask => (EntityTypes.Task, "taskId"),
            AgentToolNames.DeleteTaskList => (EntityTypes.List, "listId"),
            AgentToolNames.DeleteNote => (EntityTypes.Note, "noteId"),
            _ => ("", ""),
        };
        if (arguments is null || !arguments.TryGetValue(argumentName, out var value) || !Guid.TryParse(value?.ToString(), out var entityId)) return new AgentApprovalTarget(entityType, null, "");

        try
        {
            var title = entityType switch
            {
                EntityTypes.Task => (await taskService.GetAsync(UserId, entityId, cancellationToken)).Title,
                EntityTypes.List => (await taskListService.GetAsync(UserId, entityId, cancellationToken)).Name,
                EntityTypes.Note => (await noteService.GetAsync(UserId, entityId, cancellationToken)).Title,
                _ => "",
            };
            return new AgentApprovalTarget(entityType, entityId, title);
        }
        catch (ResourceNotFoundException) { return new AgentApprovalTarget(entityType, entityId, ""); }
    }

    /// <summary>The local time of a user in the given IANA time zone, falling back to UTC for unknown zones.</summary>
    public static AgentCurrentTime CreateCurrentTime(string timeZoneId, DateTimeOffset utcNow)
    {
        var isKnownTimeZone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone);
        var localNow = isKnownTimeZone ? TimeZoneInfo.ConvertTime(utcNow, timeZone!) : utcNow.ToUniversalTime();
        var offset = localNow.Offset;
        var offsetText = $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}";
        return new AgentCurrentTime(localNow.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), localNow.DayOfWeek.ToString(), isKnownTimeZone ? timeZoneId : "UTC", offsetText);
    }

    /// <summary>Parses a local date and time such as <c>2031-03-12T09:30</c>, or throws a validation error the model can correct.</summary>
    public static DateTime ParseLocalDateTime(string? value)
    {
        string[] formats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];
        if (DateTime.TryParseExact(value?.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDateTime)) return localDateTime;
        throw new RequestValidationException("Give the reminder time as local date and time in the form yyyy-MM-ddTHH:mm.");
    }

    /// <summary>The repeat rule for a frequency name, where <c>weekdays</c> is weekly on Monday to Friday and <c>none</c> stops repeating.</summary>
    public static TaskRecurrence? CreateRecurrence(string? frequency, int interval, IReadOnlyList<string>? daysOfWeek)
    {
        var frequencyName = frequency?.Trim().ToLowerInvariant() ?? "";
        if (frequencyName is "none" or "") return null;
        if (frequencyName == "weekdays") return new TaskRecurrence(RecurrenceFrequency.Weekly, interval, TaskRecurrenceRules.Weekdays);
        if (!Enum.TryParse<RecurrenceFrequency>(frequencyName, true, out var recurrenceFrequency) || !Enum.IsDefined(recurrenceFrequency) || int.TryParse(frequencyName, out _)) throw new RequestValidationException("The frequency must be daily, weekdays, weekly, monthly, yearly or none.");

        List<DayOfWeek> days = [];
        foreach (var dayName in daysOfWeek ?? [])
        {
            if (!Enum.TryParse<DayOfWeek>(dayName.Trim(), true, out var dayOfWeek) || !Enum.IsDefined(dayOfWeek) || int.TryParse(dayName, out _)) throw new RequestValidationException($"'{dayName}' is not a day of the week.");
            days.Add(dayOfWeek);
        }
        return new TaskRecurrence(recurrenceFrequency, interval, days);
    }

    /// <summary>Runs one tool: reports its start and completion and turns domain errors into an error result for the model.</summary>
    private async Task<object> RunAsync(string toolName, Func<Task<AgentToolOutcome>> operation)
    {
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? Guid.CreateVersion7().ToString("N");
        await context.NotifyToolStartedAsync(new ToolStartedEventData(callId, toolName));
        AgentToolOutcome outcome;
        try { outcome = await operation(); }
        catch (DomainException exception)
        {
            await context.NotifyToolCompletedAsync(new ToolCompletedEventData(callId, toolName, false, null, exception.Message));
            return new AgentToolError(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await context.NotifyToolCompletedAsync(new ToolCompletedEventData(callId, toolName, false, null, "The tool failed."));
            throw;
        }

        await context.NotifyToolCompletedAsync(new ToolCompletedEventData(callId, toolName, true, outcome.AgentActionId, null));
        return outcome.Result;
    }

    /// <summary>Runs a change of one item through its service and records an agent action with the item's stored form before and after.</summary>
    private async Task<AgentToolOutcome> ChangeAsync<TResult>(string toolName, string entityType, Guid? entityId, Func<Task<TResult>> operation, Func<TResult, Guid> getEntityId, CancellationToken cancellationToken) where TResult : notnull
    {
        var before = entityId is { } existingId ? await agentActionService.CaptureAsync(entityType, UserId, existingId, cancellationToken) : null;
        var result = await operation();
        var entity = await CreateEntityAsync(entityType, getEntityId(result), before, cancellationToken);
        var agentActionId = await agentActionService.RecordAsync(UserId, context.ConversationId, toolName, GetCurrentArgumentsJson(), [entity], cancellationToken);
        return new AgentToolOutcome(result, agentActionId);
    }

    private async Task<AgentActionEntityDocument> CreateEntityAsync(string entityType, Guid entityId, BsonDocument? before, CancellationToken cancellationToken)
    {
        var after = await agentActionService.CaptureAsync(entityType, UserId, entityId, cancellationToken) ?? throw new InvalidOperationException($"The changed {entityType} was not found.");
        return new AgentActionEntityDocument { EntityType = entityType, EntityId = entityId, Before = before, After = after };
    }

    private static string GetCurrentArgumentsJson()
    {
        var arguments = FunctionInvokingChatClient.CurrentContext?.Arguments ?? AgentToolInvocation.Current?.Arguments;
        return arguments is null ? "{}" : JsonSerializer.Serialize<IDictionary<string, object?>>(arguments, AgentJsonSerializerOptions.Default);
    }
}
