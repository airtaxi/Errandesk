using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class RecurrenceEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    /// <summary>2031-03-10 21:00 in Asia/Seoul, a Monday.</summary>
    private static readonly DateOnly s_seoulToday = new(2031, 3, 10);

    [Fact]
    public async Task SettingRecurrenceWithoutDueDate_SetsTheDueDateToToday()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");

        var created = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants", Recurrence: new TaskRecurrence(RecurrenceFrequency.Daily, 2)), HttpStatusCode.Created);
        var plain = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Stretch"), HttpStatusCode.Created);
        var patched = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{plain.Id}", new UpdateTaskRequest { Recurrence = new TaskRecurrence(RecurrenceFrequency.Weekly, 1, [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday]) });

        Assert.Equal(s_seoulToday, created.DueDate);
        Assert.Equal(RecurrenceFrequency.Daily, created.Recurrence?.Frequency);
        Assert.Equal(2, created.Recurrence?.Interval);
        Assert.Equal([], created.Recurrence?.DaysOfWeek ?? [DayOfWeek.Sunday]);
        Assert.Equal(s_seoulToday, patched.DueDate);
        Assert.Equal(RecurrenceFrequency.Weekly, patched.Recurrence?.Frequency);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], patched.Recurrence?.DaysOfWeek);
        JsonAssert.Equal(patched, await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{plain.Id}"));
    }

    [Fact]
    public async Task RecurrenceAndReminder_AreSerializedWithStringNames()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var reminderAt = new DateTimeOffset(2031, 3, 11, 8, 15, 0, TimeSpan.FromHours(9));

        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Team sync", DueDate: new DateOnly(2031, 3, 11), ReminderAt: reminderAt, Recurrence: new TaskRecurrence(RecurrenceFrequency.Weekly, 1, [DayOfWeek.Tuesday])), HttpStatusCode.Created);
        var json = await alice.Client.SendExpectingAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.OK);

        Assert.Contains("\"frequency\":\"weekly\"", json);
        Assert.Contains("\"reminderAt\":\"2031-03-10T23:15:00+00:00\"", json);
        Assert.Contains("\"nextOccurrenceId\":null", json);
        Assert.Equal(reminderAt, task.ReminderAt);
        Assert.Equal(TimeSpan.Zero, task.ReminderAt?.Offset);
    }

    [Fact]
    public async Task CompletingARepeatingTask_CreatesTheNextOccurrenceWithResetStepsAndShiftedReminder()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var list = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Home"), HttpStatusCode.Created);
        var reminderAt = new DateTimeOffset(2031, 3, 10, 0, 30, 0, TimeSpan.Zero);
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Take out trash", ListId: list.Id, Body: "Both bins", IsImportant: true, DueDate: s_seoulToday, IsInMyDay: true, Steps: [new CreateTaskStepRequest("Kitchen", IsCompleted: true), new CreateTaskStepRequest("Bathroom")], ReminderAt: reminderAt, Recurrence: new TaskRecurrence(RecurrenceFrequency.Weekly, 1, [DayOfWeek.Monday, DayOfWeek.Thursday])), HttpStatusCode.Created);

        var completed = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true });

        Assert.True(completed.IsCompleted);
        var nextOccurrenceId = Assert.NotNull(completed.NextOccurrenceId);
        Assert.NotEqual(task.Id, nextOccurrenceId);
        var next = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{nextOccurrenceId}");
        Assert.Equal("Take out trash", next.Title);
        Assert.Equal("Both bins", next.Body);
        Assert.Equal(list.Id, next.ListId);
        Assert.True(next.IsImportant);
        Assert.False(next.IsCompleted);
        Assert.False(next.IsInMyDay);
        Assert.Equal(new DateOnly(2031, 3, 13), next.DueDate);
        Assert.Equal(reminderAt.AddDays(3), next.ReminderAt);
        Assert.Equal(RecurrenceFrequency.Weekly, next.Recurrence?.Frequency);
        Assert.Equal(1, next.Recurrence?.Interval);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], next.Recurrence?.DaysOfWeek);
        Assert.Null(next.NextOccurrenceId);
        Assert.Equal(["Kitchen", "Bathroom"], next.Steps.Select(step => step.Title));
        Assert.All(next.Steps, step => Assert.False(step.IsCompleted));
        Assert.Empty(next.Steps.Select(step => step.Id).Intersect(task.Steps.Select(step => step.Id)));
    }

    [Fact]
    public async Task CompletingAgainAfterReopening_DoesNotCreateAnotherOccurrence()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay rent", DueDate: new DateOnly(2031, 3, 1), Recurrence: new TaskRecurrence(RecurrenceFrequency.Monthly)), HttpStatusCode.Created);
        var path = $"{TasksPath}/{task.Id}";

        var firstCompletion = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = true });
        var reopened = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = false });
        var secondCompletion = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = true });
        var completedAgainWithoutReopening = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = true });

        Assert.Equal(firstCompletion.NextOccurrenceId, reopened.NextOccurrenceId);
        Assert.Equal(firstCompletion.NextOccurrenceId, secondCompletion.NextOccurrenceId);
        Assert.Equal(firstCompletion.NextOccurrenceId, completedAgainWithoutReopening.NextOccurrenceId);
        var allTasks = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?includeCompleted=true");
        Assert.Equal(2, allTasks.Count(item => item.Title == "Pay rent"));
        Assert.Equal(new DateOnly(2031, 4, 1), Assert.Single(allTasks, item => !item.IsCompleted).DueDate);
    }

    [Fact]
    public async Task ConcurrentCompletions_CreateOneOccurrence()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Stand-up", DueDate: new DateOnly(2031, 3, 10), Recurrence: new TaskRecurrence(RecurrenceFrequency.Weekly, 1, TaskRecurrenceRules.Weekdays)), HttpStatusCode.Created);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true })));
        List<TaskItemResponse> completions = [];
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
                if (response.StatusCode == HttpStatusCode.OK) completions.Add(ApiClient.DeserializeJson<TaskItemResponse>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
            }
        }

        Assert.NotEmpty(completions);
        Assert.Single(completions.Select(completion => completion.NextOccurrenceId).Distinct());
        var allTasks = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?includeCompleted=true");
        var next = Assert.Single(allTasks, item => !item.IsCompleted);
        Assert.Equal(completions[0].NextOccurrenceId, next.Id);
        Assert.Equal(new DateOnly(2031, 3, 11), next.DueDate);
    }

    [Fact]
    public async Task CompletingMonthlyOccurrences_ReturnsToTheMonthEndAnchor()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Close the books", DueDate: new DateOnly(2031, 1, 31), Recurrence: new TaskRecurrence(RecurrenceFrequency.Monthly)), HttpStatusCode.Created);

        List<DateOnly?> dueDates = [];
        for (var occurrence = 0; occurrence < 3; occurrence++)
        {
            var completed = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true });
            task = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{completed.NextOccurrenceId}");
            dueDates.Add(task.DueDate);
        }

        Assert.Equal([new DateOnly(2031, 2, 28), new DateOnly(2031, 3, 31), new DateOnly(2031, 4, 30)], dueDates);
    }

    [Fact]
    public async Task DeletedNextOccurrence_IsNotCreatedAgain()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Gym", Recurrence: new TaskRecurrence(RecurrenceFrequency.Daily)), HttpStatusCode.Created);
        var path = $"{TasksPath}/{task.Id}";
        var completed = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = true });
        await alice.Client.DeleteAsync($"{TasksPath}/{completed.NextOccurrenceId}");

        await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = false });
        await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsCompleted = true });

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{completed.NextOccurrenceId}", null, HttpStatusCode.NotFound);
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?includeCompleted=true"));
    }

    [Fact]
    public async Task RemovingTheDueDateOrTheRule_StopsRepeatingAndCompletionCreatesNothing()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var first = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Read", Recurrence: new TaskRecurrence(RecurrenceFrequency.Daily)), HttpStatusCode.Created);
        var second = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Write", Recurrence: new TaskRecurrence(RecurrenceFrequency.Yearly)), HttpStatusCode.Created);

        var withoutDueDate = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{first.Id}", new UpdateTaskRequest { DueDate = null });
        var withoutRule = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{second.Id}", new UpdateTaskRequest { Recurrence = null });
        var completedFirst = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{first.Id}", new UpdateTaskRequest { IsCompleted = true });
        var completedSecond = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{second.Id}", new UpdateTaskRequest { IsCompleted = true });

        Assert.Null(withoutDueDate.DueDate);
        Assert.Null(withoutDueDate.Recurrence);
        Assert.Null(withoutRule.Recurrence);
        Assert.NotNull(withoutRule.DueDate);
        Assert.Null(completedFirst.NextOccurrenceId);
        Assert.Null(completedSecond.NextOccurrenceId);
        Assert.Equal(2, (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?includeCompleted=true")).Count);
    }

    [Fact]
    public async Task InvalidRulesAndReminderTimes_AreRejected()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plan"), HttpStatusCode.Created);

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Bad", Recurrence: new TaskRecurrence(RecurrenceFrequency.Daily, 0)), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Recurrence = new TaskRecurrence(RecurrenceFrequency.Monthly, 1, [DayOfWeek.Monday]) }, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { ReminderAt = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero) }, HttpStatusCode.BadRequest);
        Assert.Equal(task.ChangeVersion, (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).ChangeVersion);
    }

    [Fact]
    public async Task Reminder_CanBeSetChangedAndRemoved()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call mom"), HttpStatusCode.Created);
        var path = $"{TasksPath}/{task.Id}";
        var reminderAt = ApiTestServer.DefaultStartTime.AddHours(2);

        var withReminder = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { ReminderAt = reminderAt });
        var otherChange = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { IsImportant = true });
        var withoutReminder = await alice.Client.PatchAsync<TaskItemResponse>(path, new UpdateTaskRequest { ReminderAt = null });

        Assert.Equal(reminderAt, withReminder.ReminderAt);
        Assert.Equal(reminderAt, otherChange.ReminderAt);
        Assert.Null(withoutReminder.ReminderAt);
        var stored = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.Null(stored.ReminderAt);
        Assert.Null(stored.ReminderSetByUserId);
    }
}
