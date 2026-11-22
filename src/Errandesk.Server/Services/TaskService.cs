using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Creates, queries, changes and deletes tasks and the steps inside them. A user sees the tasks of the lists they own or are a member of; My Day is personal to each user.
/// </summary>
public sealed class TaskService(ErrandeskDatabase database, TaskListService taskListService, TaskListAccess taskListAccess, UserService userService, NoteService noteService, DocumentUpdater documentUpdater)
{
    private const string ResourceName = "task";
    private const string StepResourceName = "step";

    /// <summary>
    /// Tasks of a view: <see cref="TaskViews.MyDay"/>, <see cref="TaskViews.Important"/>, <see cref="TaskViews.Planned"/>, a list ID, or every task when <paramref name="view"/> is empty.
    /// Open tasks come first in their view order, followed by completed tasks, most recently completed first, when <paramref name="includeCompleted"/> is set.
    /// </summary>
    public async Task<IReadOnlyList<TaskItemResponse>> QueryAsync(Guid userId, string? view, bool includeCompleted, CancellationToken cancellationToken)
    {
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        var filterBuilder = Builders<TaskDocument>.Filter;
        var filter = await taskListAccess.CreateVisibleTasksFilterAsync(userId, cancellationToken);
        if (!includeCompleted) filter &= filterBuilder.Eq(task => task.IsCompleted, false);

        var normalizedView = view?.Trim().ToLowerInvariant() ?? "";
        if (Guid.TryParse(normalizedView, out var listId))
        {
            await taskListService.GetVisibleAsync(userId, listId, cancellationToken);
            filter &= filterBuilder.Eq(task => task.ListId, listId);
        }
        else
        {
            filter &= normalizedView switch
            {
                "" => filterBuilder.Empty,
                TaskViews.MyDay => (filterBuilder.Eq(task => task.OwnerUserId, userId) & filterBuilder.Eq(task => task.MyDayDate, today)) | filterBuilder.ElemMatch(task => task.MemberMyDayDates, entry => entry.UserId == userId && entry.Date == today),
                TaskViews.Important => filterBuilder.Eq(task => task.IsImportant, true),
                TaskViews.Planned => filterBuilder.Ne(task => task.DueDate, null),
                _ => throw new RequestValidationException($"The view must be {TaskViews.MyDay}, {TaskViews.Important}, {TaskViews.Planned} or a list ID."),
            };
        }

        var tasks = await database.Tasks.Find(filter).ToListAsync(cancellationToken);
        var openTasks = tasks.Where(task => !task.IsCompleted);
        var orderedOpenTasks = normalizedView == TaskViews.Planned ? openTasks.OrderBy(task => task.DueDate).ThenBy(task => task.SortOrder) : openTasks.OrderBy(task => task.SortOrder);
        var orderedCompletedTasks = tasks.Where(task => task.IsCompleted).OrderByDescending(task => task.CompletedAt);
        var linkedNotes = await noteService.GetLinkedNoteSummariesAsync(userId, [.. tasks.Select(task => task.Id)], cancellationToken);
        return [.. orderedOpenTasks.ThenBy(task => task.CreatedAt).Concat(orderedCompletedTasks).Select(task => task.ToResponse(userId, today, linkedNotes.GetValueOrDefault(task.Id) ?? []))];
    }

    /// <summary>
    /// Tasks in which every word of <paramref name="query"/> appears, ignoring case, in the title, the note or a step, in the order of <see cref="QueryAsync"/>.
    /// Without a query, every task of the list, or of all lists when <paramref name="listId"/> is <see langword="null"/>.
    /// </summary>
    public async Task<IReadOnlyList<TaskItemResponse>> SearchAsync(Guid userId, string? query, Guid? listId, bool includeCompleted, CancellationToken cancellationToken)
    {
        var searchText = InputValidation.SearchQuery(query);
        var tasks = await QueryAsync(userId, listId?.ToString(), includeCompleted, cancellationToken);
        if (searchText is null) return tasks;

        var terms = searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return [.. tasks.Where(task => terms.All(term => task.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || task.Body.Contains(term, StringComparison.OrdinalIgnoreCase) || task.Steps.Any(step => step.Title.Contains(term, StringComparison.OrdinalIgnoreCase))))];
    }

    /// <summary>Open tasks whose due date is before today in the user's time zone, earliest first.</summary>
    public async Task<IReadOnlyList<TaskItemResponse>> GetOverdueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        var plannedTasks = await QueryAsync(userId, TaskViews.Planned, false, cancellationToken);
        return [.. plannedTasks.Where(task => task.DueDate < today)];
    }

    public async Task<TaskItemResponse> GetAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
    {
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        return await ToResponseAsync(userId, await GetVisibleAsync(userId, taskId, cancellationToken), today, cancellationToken);
    }

    public async Task<CreateResult<TaskItemResponse>> CreateAsync(Guid userId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var title = InputValidation.RequireText(request.Title, "title", InputValidation.MaximumTaskTitleLength);
        var body = InputValidation.OptionalText(request.Body, "body", InputValidation.MaximumTaskBodyLength);
        var taskId = InputValidation.ClientId(request.Id);
        var steps = CreateSteps(request.Steps ?? []);
        var reminderAt = request.ReminderAt is { } requestedReminderAt ? InputValidation.ReminderTime(requestedReminderAt) : (DateTime?)null;
        var recurrence = request.Recurrence is { } requestedRecurrence ? TaskRecurrenceRules.Normalize(requestedRecurrence) : null;
        var list = request.ListId is { } listId ? await taskListService.GetVisibleAsync(userId, listId, cancellationToken) : await taskListService.EnsureDefaultListAsync(userId, cancellationToken);
        var sortOrder = request.SortOrder is { } requestedSortOrder ? InputValidation.SortOrder(requestedSortOrder) : await GetTopSortOrderAsync(list.Id, cancellationToken);
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        var dueDate = recurrence is null ? request.DueDate : request.DueDate ?? RecurrenceCalculator.GetFirstDueDate(today, recurrence);

        var task = new TaskDocument
        {
            Id = taskId,
            OwnerUserId = list.OwnerUserId,
            ListId = list.Id,
            Title = title,
            Body = body,
            IsImportant = request.IsImportant,
            IsCompleted = request.IsCompleted,
            CompletedAt = request.IsCompleted ? documentUpdater.GetUtcNow() : null,
            DueDate = dueDate,
            SortOrder = sortOrder,
            Steps = steps,
            Recurrence = recurrence is not null && dueDate is { } recurrenceDueDate ? TaskRecurrenceRules.ToDocument(recurrence, recurrenceDueDate) : null,
        };
        task.SetMyDayDate(userId, request.IsInMyDay ? today : null);
        task.SetReminder(reminderAt, userId);
        var result = await documentUpdater.InsertIdempotentAsync(database.Tasks, task, list.OwnerUserId, ResourceName, cancellationToken);
        if (!result.IsCreated && !await taskListAccess.CanAccessListAsync(userId, result.Resource.ListId, cancellationToken)) throw new ResourceConflictException("Another task already uses this ID.");
        return new CreateResult<TaskItemResponse>(await ToResponseAsync(userId, result.Resource, today, cancellationToken), result.IsCreated);
    }

    public async Task<TaskItemResponse> UpdateAsync(Guid userId, Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title.IsSpecified ? InputValidation.RequireText(request.Title.Value, "title", InputValidation.MaximumTaskTitleLength) : null;
        var body = request.Body.IsSpecified ? InputValidation.OptionalText(request.Body.Value, "body", InputValidation.MaximumTaskBodyLength) : null;
        var sortOrder = request.SortOrder.IsSpecified ? InputValidation.SortOrder(request.SortOrder.Value) : (double?)null;
        var reminderAt = request.ReminderAt.IsSpecified && request.ReminderAt.Value is { } requestedReminderAt ? InputValidation.ReminderTime(requestedReminderAt) : (DateTime?)null;
        var recurrence = request.Recurrence.IsSpecified && request.Recurrence.Value is { } requestedRecurrence ? TaskRecurrenceRules.Normalize(requestedRecurrence) : null;
        var targetList = request.ListId.IsSpecified ? await taskListService.GetVisibleAsync(userId, request.ListId.Value, cancellationToken) : null;
        Guid? previousListId = null;
        IReadOnlyList<Guid> usersLosingAccess = [];
        if (targetList is not null)
        {
            previousListId = (await GetVisibleAsync(userId, taskId, cancellationToken)).ListId;
            usersLosingAccess = await taskListAccess.GetUsersLosingAccessAsync(previousListId.Value, targetList.Id, cancellationToken);
        }

        var today = await userService.GetTodayAsync(userId, cancellationToken);
        var utcNow = documentUpdater.GetUtcNow();
        var nextOccurrenceId = Guid.CreateVersion7();

        var task = await UpdateTaskAsync(userId, taskId, request.ExpectedChangeVersion, today, current =>
        {
            if (targetList is not null && current.ListId != targetList.Id) MoveToList(current, targetList, current.ListId == previousListId ? usersLosingAccess : [current.OwnerUserId]);
            if (title is not null) current.Title = title;
            if (body is not null) current.Body = body;
            if (request.IsImportant.IsSpecified) current.IsImportant = request.IsImportant.Value;
            if (request.DueDate.IsSpecified) SetDueDate(current, request.DueDate.Value);
            if (request.Recurrence.IsSpecified) SetRecurrence(current, recurrence, today);
            if (request.ReminderAt.IsSpecified) current.SetReminder(reminderAt, userId);
            if (request.IsInMyDay.IsSpecified) current.SetMyDayDate(userId, request.IsInMyDay.Value ? today : null);
            if (sortOrder is { } newSortOrder) current.SortOrder = newSortOrder;
            if (request.IsCompleted.IsSpecified) SetCompletion(current, request.IsCompleted.Value, utcNow, nextOccurrenceId);
        }, cancellationToken);
        if (usersLosingAccess.Count > 0) taskListAccess.NotifyUsers(usersLosingAccess, task.ChangeVersion);
        if (request.IsCompleted is { IsSpecified: true, Value: true }) await EnsureNextOccurrenceAsync(userId, task, cancellationToken);
        return await ToResponseAsync(userId, task, today, cancellationToken);
    }

    /// <summary>
    /// Creates the next occurrence of a completed repeating task under the ID stored on the task: the next due date, the reminder moved by the same number of days, open steps and no My Day.
    /// Does nothing when the occurrence exists or was deleted.
    /// </summary>
    private async Task EnsureNextOccurrenceAsync(Guid userId, TaskDocument task, CancellationToken cancellationToken)
    {
        if (!task.IsCompleted || task.NextOccurrenceId is not { } nextOccurrenceId || task.Recurrence is null || task.DueDate is not { } dueDate) return;

        var nextDueDate = RecurrenceCalculator.GetNextDueDate(dueDate, task.Recurrence);
        var nextOccurrence = new TaskDocument
        {
            Id = nextOccurrenceId,
            ListId = task.ListId,
            Title = task.Title,
            Body = task.Body,
            IsImportant = task.IsImportant,
            DueDate = nextDueDate,
            SortOrder = await GetTopSortOrderAsync(task.ListId, cancellationToken),
            Steps = [.. task.Steps.Select(step => new TaskStepDocument { Id = Guid.CreateVersion7(), Title = step.Title, SortOrder = step.SortOrder })],
            Recurrence = task.Recurrence.Copy(),
        };
        if (task.ReminderAt is { } reminderAt)
        {
            var reminderUserId = task.ReminderSetByUserId ?? userId;
            var reminderUser = await userService.GetAsync(reminderUserId, cancellationToken);
            nextOccurrence.SetReminder(UserTimeZones.AddLocalDays(reminderAt, nextDueDate.DayNumber - dueDate.DayNumber, reminderUser.TimeZone), reminderUserId);
        }

        try { await documentUpdater.InsertIdempotentAsync(database.Tasks, nextOccurrence, task.OwnerUserId, ResourceName, cancellationToken); }
        catch (ResourceConflictException) { }
    }

    public async Task DeleteAsync(Guid userId, Guid taskId, CancellationToken cancellationToken) => await documentUpdater.SoftDeleteAsync(database.Tasks, await taskListAccess.CreateVisibleTaskFilterAsync(userId, taskId, cancellationToken), ResourceName, cancellationToken);

    /// <summary>Adds a step at the end of the task, or returns the task unchanged when a step with the same ID exists.</summary>
    public async Task<TaskItemResponse> AddStepAsync(Guid userId, Guid taskId, CreateTaskStepRequest request, CancellationToken cancellationToken)
    {
        var title = InputValidation.RequireText(request.Title, "step title", InputValidation.MaximumStepTitleLength);
        var stepId = InputValidation.ClientId(request.Id);
        var requestedSortOrder = request.SortOrder is { } sortOrder ? InputValidation.SortOrder(sortOrder) : (double?)null;
        var today = await userService.GetTodayAsync(userId, cancellationToken);

        var existingTask = await GetVisibleAsync(userId, taskId, cancellationToken);
        if (existingTask.Steps.Any(step => step.Id == stepId)) return await ToResponseAsync(userId, existingTask, today, cancellationToken);

        var task = await UpdateTaskAsync(userId, taskId, null, today, current =>
        {
            if (current.Steps.Any(step => step.Id == stepId)) return;
            if (current.Steps.Count >= InputValidation.MaximumStepCount) throw new RequestValidationException($"A task can have at most {InputValidation.MaximumStepCount} steps.");
            current.Steps.Add(new TaskStepDocument { Id = stepId, Title = title, IsCompleted = request.IsCompleted, SortOrder = requestedSortOrder ?? GetNextStepSortOrder(current.Steps) });
        }, cancellationToken);
        return await ToResponseAsync(userId, task, today, cancellationToken);
    }

    public async Task<TaskItemResponse> UpdateStepAsync(Guid userId, Guid taskId, Guid stepId, UpdateTaskStepRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title.IsSpecified ? InputValidation.RequireText(request.Title.Value, "step title", InputValidation.MaximumStepTitleLength) : null;
        var sortOrder = request.SortOrder.IsSpecified ? InputValidation.SortOrder(request.SortOrder.Value) : (double?)null;
        var today = await userService.GetTodayAsync(userId, cancellationToken);

        var task = await UpdateTaskAsync(userId, taskId, request.ExpectedChangeVersion, today, current =>
        {
            var step = current.Steps.Find(step => step.Id == stepId) ?? throw new ResourceNotFoundException(StepResourceName);
            if (title is not null) step.Title = title;
            if (request.IsCompleted.IsSpecified) step.IsCompleted = request.IsCompleted.Value;
            if (sortOrder is { } newSortOrder) step.SortOrder = newSortOrder;
        }, cancellationToken);
        return await ToResponseAsync(userId, task, today, cancellationToken);
    }

    public async Task<TaskItemResponse> DeleteStepAsync(Guid userId, Guid taskId, Guid stepId, CancellationToken cancellationToken)
    {
        var today = await userService.GetTodayAsync(userId, cancellationToken);
        var task = await UpdateTaskAsync(userId, taskId, null, today, current =>
        {
            if (current.Steps.RemoveAll(step => step.Id == stepId) == 0)
            {
                throw new ResourceNotFoundException(StepResourceName);
            }
        }, cancellationToken);
        return await ToResponseAsync(userId, task, today, cancellationToken);
    }

    private async Task<TaskItemResponse> ToResponseAsync(Guid userId, TaskDocument task, DateOnly today, CancellationToken cancellationToken)
    {
        var linkedNotes = await noteService.GetLinkedNoteSummariesAsync(userId, [task.Id], cancellationToken);
        return task.ToResponse(userId, today, linkedNotes.GetValueOrDefault(task.Id) ?? []);
    }

    private async Task<TaskDocument> UpdateTaskAsync(Guid userId, Guid taskId, long? expectedChangeVersion, DateOnly today, Action<TaskDocument> applyChanges, CancellationToken cancellationToken)
    {
        try { return await documentUpdater.UpdateAsync(database.Tasks, await taskListAccess.CreateVisibleTaskFilterAsync(userId, taskId, cancellationToken), ResourceName, expectedChangeVersion, current => current, applyChanges, cancellationToken); }
        catch (ChangeVersionConflictException exception) when (exception.CurrentResource is TaskDocument currentTask) { throw new ChangeVersionConflictException(await ToResponseAsync(userId, currentTask, today, cancellationToken)); }
    }

    private async Task<TaskDocument> GetVisibleAsync(Guid userId, Guid taskId, CancellationToken cancellationToken) => await database.Tasks.Find(await taskListAccess.CreateVisibleTaskFilterAsync(userId, taskId, cancellationToken)).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    private async Task<double> GetTopSortOrderAsync(Guid listId, CancellationToken cancellationToken)
    {
        var firstTask = await database.Tasks.Find(task => task.ListId == listId && !task.IsDeleted).SortBy(task => task.SortOrder).FirstOrDefaultAsync(cancellationToken);
        return firstTask is null ? 0 : Math.Ceiling(firstTask.SortOrder) - 1;
    }

    private static List<TaskStepDocument> CreateSteps(IReadOnlyList<CreateTaskStepRequest> stepRequests)
    {
        if (stepRequests.Count > InputValidation.MaximumStepCount) throw new RequestValidationException($"A task can have at most {InputValidation.MaximumStepCount} steps.");

        List<TaskStepDocument> steps = [];
        foreach (var stepRequest in stepRequests)
        {
            var stepId = InputValidation.ClientId(stepRequest.Id);
            if (steps.Any(step => step.Id == stepId)) throw new RequestValidationException("Step IDs must be unique within a task.");

            var title = InputValidation.RequireText(stepRequest.Title, "step title", InputValidation.MaximumStepTitleLength);
            var sortOrder = stepRequest.SortOrder is { } requestedSortOrder ? InputValidation.SortOrder(requestedSortOrder) : GetNextStepSortOrder(steps);
            steps.Add(new TaskStepDocument { Id = stepId, Title = title, IsCompleted = stepRequest.IsCompleted, SortOrder = sortOrder });
        }
        return steps;
    }

    /// <summary>
    /// Moves the task into <paramref name="targetList"/>, whose owner becomes the task's owner. Users who lose sight of the task are recorded so the change feed tells them to drop it,
    /// and users who can see the target list are taken off that record.
    /// </summary>
    private static void MoveToList(TaskDocument task, TaskListDocument targetList, IEnumerable<Guid> usersLosingAccess)
    {
        var targetAudience = targetList.GetAudience().ToHashSet();
        task.ListId = targetList.Id;
        task.ChangeOwner(targetList.OwnerUserId);
        task.RevokedUserIds = [.. task.RevokedUserIds.Union(usersLosingAccess).Where(revokedUserId => !targetAudience.Contains(revokedUserId))];
    }

    private static double GetNextStepSortOrder(List<TaskStepDocument> steps) => steps.Count == 0 ? 0 : Math.Floor(steps.Max(step => step.SortOrder)) + 1;

    /// <summary>Completes or reopens the task. Completing a repeating task for the first time reserves <paramref name="nextOccurrenceId"/> for its next occurrence.</summary>
    private static void SetCompletion(TaskDocument task, bool isCompleted, DateTime utcNow, Guid nextOccurrenceId)
    {
        if (isCompleted && task.Recurrence is not null && task.DueDate is not null) task.NextOccurrenceId ??= nextOccurrenceId;
        if (isCompleted == task.IsCompleted) return;
        task.IsCompleted = isCompleted;
        task.CompletedAt = isCompleted ? utcNow : null;
    }

    /// <summary>Sets the due date and anchors the repeat rule on it; removing the due date stops the task from repeating.</summary>
    private static void SetDueDate(TaskDocument task, DateOnly? dueDate)
    {
        task.DueDate = dueDate;
        if (dueDate is null) task.Recurrence = null;
        else if (task.Recurrence is not null) task.Recurrence.AnchorDayOfMonth = dueDate.Value.Day;
    }

    /// <summary>Sets or removes the repeat rule. A repeating task without a due date becomes due <paramref name="today"/>, or on the first chosen day of a weekly rule from today on.</summary>
    private static void SetRecurrence(TaskDocument task, TaskRecurrence? normalizedRecurrence, DateOnly today)
    {
        if (normalizedRecurrence is null)
        {
            task.Recurrence = null;
            return;
        }

        task.DueDate ??= RecurrenceCalculator.GetFirstDueDate(today, normalizedRecurrence);
        task.Recurrence = TaskRecurrenceRules.ToDocument(normalizedRecurrence, task.DueDate.Value);
    }
}
