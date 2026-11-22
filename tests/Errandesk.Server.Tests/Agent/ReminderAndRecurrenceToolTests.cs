using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Errandesk.Server.Tests.Agent;

public sealed class ReminderAndRecurrenceToolTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task SetReminder_ConvertsTheLocalTimeOfTheUserAndCanRemoveTheReminder()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call the bank"), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = CreateToolset(server, context);
        var cancellationToken = TestContext.Current.CancellationToken;

        var setResult = Assert.IsType<AgentReminderResult>(await toolset.SetReminderAsync(task.Id, "2031-03-11T09:30", cancellationToken: cancellationToken));
        var stored = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        var removeResult = Assert.IsType<AgentReminderResult>(await toolset.SetReminderAsync(task.Id, remove: true, cancellationToken: cancellationToken));

        Assert.Equal(new AgentReminderResult(task.Id, "Call the bank", "2031-03-11T09:30", "Asia/Seoul"), setResult);
        Assert.Equal(new DateTimeOffset(2031, 3, 11, 0, 30, 0, TimeSpan.Zero), stored.ReminderAt);
        Assert.Null(removeResult.ReminderLocalTime);
        Assert.Null((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).ReminderAt);
        Assert.All(context.ToolCalls, toolCall => Assert.NotNull(toolCall.AgentActionId));

        var firstAction = Assert.NotNull(context.ToolCalls[0].AgentActionId);
        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, $"/api/agent-actions/{context.ToolCalls[1].AgentActionId}/undo");
        Assert.Equal(stored.ReminderAt, (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).ReminderAt);
        Assert.NotEqual(Guid.Empty, firstAction);
    }

    [Fact]
    public async Task SetReminder_ReturnsAnErrorForAnUnreadableTime()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Call the bank"), HttpStatusCode.Created);
        var toolset = CreateToolset(server, new AgentRunContext(alice.Id, null));

        Assert.IsType<AgentToolError>(await toolset.SetReminderAsync(task.Id, "tomorrow morning", cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<AgentToolError>(await toolset.SetReminderAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.IsType<AgentToolError>(await toolset.SetReminderAsync(Guid.CreateVersion7(), "2031-03-11T09:30", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetRecurrence_SetsWeekdaysWeeklyDaysAndStopsRepeating()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Stand-up"), HttpStatusCode.Created);
        var toolset = CreateToolset(server, new AgentRunContext(alice.Id, null));
        var cancellationToken = TestContext.Current.CancellationToken;

        var weekdays = Assert.IsType<TaskItemResponse>(await toolset.SetRecurrenceAsync(task.Id, "weekdays", cancellationToken: cancellationToken));
        var weekly = Assert.IsType<TaskItemResponse>(await toolset.SetRecurrenceAsync(task.Id, "Weekly", 2, ["thursday", "Monday"], cancellationToken));
        var stopped = Assert.IsType<TaskItemResponse>(await toolset.SetRecurrenceAsync(task.Id, "none", cancellationToken: cancellationToken));

        Assert.Equal(new DateOnly(2031, 3, 10), weekdays.DueDate);
        Assert.Equal(RecurrenceFrequency.Weekly, weekdays.Recurrence?.Frequency);
        Assert.Equal(TaskRecurrenceRules.Weekdays, weekdays.Recurrence?.DaysOfWeek);
        Assert.Equal(2, weekly.Recurrence?.Interval);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], weekly.Recurrence?.DaysOfWeek);
        Assert.Null(stopped.Recurrence);
        Assert.Equal(new DateOnly(2031, 3, 10), stopped.DueDate);
        Assert.IsType<AgentToolError>(await toolset.SetRecurrenceAsync(task.Id, "hourly", cancellationToken: cancellationToken));
        Assert.IsType<AgentToolError>(await toolset.SetRecurrenceAsync(task.Id, "weekly", 1, ["someday"], cancellationToken));
        Assert.IsType<AgentToolError>(await toolset.SetRecurrenceAsync(task.Id, "daily", 0, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task CompleteTask_RecordsTheNextOccurrenceSoUndoRemovesIt()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants", DueDate: new DateOnly(2031, 3, 10), Recurrence: new TaskRecurrence(RecurrenceFrequency.Daily)), HttpStatusCode.Created);
        var context = new AgentRunContext(alice.Id, null);
        var toolset = CreateToolset(server, context);

        var completed = Assert.IsType<TaskItemResponse>(await toolset.CompleteTaskAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken));
        var nextOccurrenceId = Assert.NotNull(completed.NextOccurrenceId);
        var action = await server.FindRawAsync(server.Database.AgentActions, Assert.NotNull(Assert.Single(context.ToolCalls).AgentActionId));

        Assert.Equal([task.Id, nextOccurrenceId], action.Entities.Select(entity => entity.EntityId));
        Assert.Null(action.Entities[1].Before);
        Assert.Equal(new DateOnly(2031, 3, 11), (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{nextOccurrenceId}")).DueDate);

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, $"/api/agent-actions/{action.Id}/undo");

        var reopened = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.False(reopened.IsCompleted);
        Assert.Null(reopened.NextOccurrenceId);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{nextOccurrenceId}", null, HttpStatusCode.NotFound);
    }

    private static AgentToolset CreateToolset(ApiTestServer server, AgentRunContext context) => server.Services.GetRequiredService<AgentToolsetFactory>().Create(context);
}
