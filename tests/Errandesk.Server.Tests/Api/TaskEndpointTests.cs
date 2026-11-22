using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class TaskEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Create_WithoutListId_AddsToTheTopOfTheDefaultList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var defaultList = await server.GetDefaultListAsync(alice);

        var first = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("First", Body: "Details"), HttpStatusCode.Created);
        var second = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Second"), HttpStatusCode.Created);

        Assert.Equal(defaultList.Id, first.ListId);
        Assert.Equal("Details", first.Body);
        Assert.Equal("", second.Body);
        Assert.False(first.IsCompleted);
        Assert.Null(first.CompletedAt);
        Assert.Empty(first.Steps);
        Assert.True(second.SortOrder < first.SortOrder);
        Assert.Equal([second.Id, first.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={defaultList.Id}")).Select(task => task.Id));
        JsonAssert.Equal(first, await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{first.Id}"));
    }

    [Fact]
    public async Task Create_WithClientId_IsIdempotent()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var taskId = Guid.CreateVersion7();

        var created = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay rent", taskId), HttpStatusCode.Created);
        var repeated = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pay rent", taskId), HttpStatusCode.OK);

        Assert.Equal(taskId, created.Id);
        Assert.Equal(created.ChangeVersion, repeated.ChangeVersion);
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));

        await alice.Client.DeleteAsync($"{TasksPath}/{taskId}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Pay rent", taskId), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithInvalidValues_ReturnsBadRequestOrNotFound()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var stepId = Guid.CreateVersion7();

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("   "), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Task", Steps: [new CreateTaskStepRequest("A", stepId), new CreateTaskStepRequest("B", stepId)]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Task", ListId: Guid.CreateVersion7()), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_ChangesOnlySpecifiedFieldsAndNullClears()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Draft report", Body: "Outline first", IsImportant: true, DueDate: new DateOnly(2031, 3, 14)), HttpStatusCode.Created);

        var retitled = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Write report" });

        Assert.Equal("Write report", retitled.Title);
        Assert.Equal("Outline first", retitled.Body);
        Assert.True(retitled.IsImportant);
        Assert.Equal(new DateOnly(2031, 3, 14), retitled.DueDate);
        Assert.True(retitled.ChangeVersion > task.ChangeVersion);

        var cleared = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Body = null, DueDate = null, IsImportant = false });

        Assert.Equal("Write report", cleared.Title);
        Assert.Equal("", cleared.Body);
        Assert.Null(cleared.DueDate);
        Assert.False(cleared.IsImportant);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "" }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_WithStaleExpectedChangeVersion_ReturnsConflictWithTheCurrentTask()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Book flights", Steps: [new CreateTaskStepRequest("Compare prices")]), HttpStatusCode.Created);
        var updated = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Book flights to Busan", ExpectedChangeVersion = task.ChangeVersion });

        var taskConflict = await alice.Client.SendExpectingAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Book trains", ExpectedChangeVersion = task.ChangeVersion }, HttpStatusCode.Conflict);
        var stepConflict = await alice.Client.SendExpectingAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}/steps/{task.Steps[0].Id}", new UpdateTaskStepRequest { IsCompleted = true, ExpectedChangeVersion = task.ChangeVersion }, HttpStatusCode.Conflict);

        Assert.Equal(updated.ChangeVersion, ApiClient.DeserializeJson<TaskItemResponse>(taskConflict).ChangeVersion);
        Assert.Equal("Book flights to Busan", ApiClient.DeserializeJson<TaskItemResponse>(taskConflict).Title);
        Assert.Equal(updated.ChangeVersion, ApiClient.DeserializeJson<TaskItemResponse>(stepConflict).ChangeVersion);
        Assert.Equal("Book flights to Busan", (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).Title);
    }

    [Fact]
    public async Task Completion_SetsTheCompletionTimeAndHidesTheTaskUnlessRequested()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var defaultList = await server.GetDefaultListAsync(alice);
        var olderCompleted = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Old", IsCompleted: true), HttpStatusCode.Created);
        var open = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Open"), HttpStatusCode.Created);
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Water plants"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromHours(2), alice);

        var completed = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = true });

        Assert.True(completed.IsCompleted);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddHours(2), completed.CompletedAt);
        Assert.Equal(ApiTestServer.DefaultStartTime, olderCompleted.CompletedAt);
        Assert.Equal([open.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={defaultList.Id}")).Select(item => item.Id));
        Assert.Equal([open.Id, task.Id, olderCompleted.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={defaultList.Id}&includeCompleted=true")).Select(item => item.Id));

        var reopened = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsCompleted = false });

        Assert.False(reopened.IsCompleted);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task ImportantAndPlannedViews_CollectTasksFromEveryList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var work = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Work"), HttpStatusCode.Created);
        var importantHome = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Fix sink", IsImportant: true), HttpStatusCode.Created);
        var importantWork = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Quarterly review", ListId: work.Id, IsImportant: true, DueDate: new DateOnly(2031, 4, 1)), HttpStatusCode.Created);
        var dueSoon = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Renew passport", DueDate: new DateOnly(2031, 3, 12)), HttpStatusCode.Created);
        var completedImportant = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Done", IsImportant: true, IsCompleted: true, DueDate: new DateOnly(2031, 3, 11)), HttpStatusCode.Created);
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plain"), HttpStatusCode.Created);

        var important = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.Important}");
        var planned = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view=Planned");
        var plannedWithCompleted = await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.Planned}&includeCompleted=true");

        Assert.Equal(new[] { importantWork.Id, importantHome.Id }.Order(), important.Select(task => task.Id).Order());
        Assert.Equal([dueSoon.Id, importantWork.Id], planned.Select(task => task.Id));
        Assert.Equal([dueSoon.Id, importantWork.Id, completedImportant.Id], plannedWithCompleted.Select(task => task.Id));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}?view=someday", null, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MyDay_FollowsTheDateInTheUsersTimeZone()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, startTime: new DateTimeOffset(2031, 3, 10, 20, 0, 0, TimeSpan.Zero));
        var seoulUser = await server.RegisterAsync("minji", timeZone: "Asia/Seoul");
        var losAngelesUser = await server.RegisterInvitedAsync(seoulUser, "jordan", "America/Los_Angeles");

        var seoulTask = await seoulUser.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Morning run", IsInMyDay: true), HttpStatusCode.Created);
        var losAngelesTask = await losAngelesUser.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Lunch meeting", IsInMyDay: true), HttpStatusCode.Created);
        var otherSeoulTask = await seoulUser.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Not today"), HttpStatusCode.Created);

        Assert.Equal(new DateOnly(2031, 3, 11), seoulTask.MyDayDate);
        Assert.True(seoulTask.IsInMyDay);
        Assert.Equal(new DateOnly(2031, 3, 10), losAngelesTask.MyDayDate);
        Assert.False(otherSeoulTask.IsInMyDay);
        Assert.Equal([seoulTask.Id], (await seoulUser.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}")).Select(task => task.Id));

        await server.AdvanceTimeAsync(TimeSpan.FromHours(12), seoulUser, losAngelesUser);

        Assert.Equal([seoulTask.Id], (await seoulUser.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}")).Select(task => task.Id));
        Assert.Empty(await losAngelesUser.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}"));
        Assert.False((await losAngelesUser.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{losAngelesTask.Id}")).IsInMyDay);

        await server.AdvanceTimeAsync(TimeSpan.FromDays(1), seoulUser);

        Assert.Empty(await seoulUser.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}"));
        var readded = await seoulUser.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{seoulTask.Id}", new UpdateTaskRequest { IsInMyDay = true });
        Assert.Equal(new DateOnly(2031, 3, 12), readded.MyDayDate);
        Assert.True(readded.IsInMyDay);
        var removed = await seoulUser.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{seoulTask.Id}", new UpdateTaskRequest { IsInMyDay = false });
        Assert.Null(removed.MyDayDate);
        Assert.Empty(await seoulUser.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={TaskViews.MyDay}"));
    }

    [Fact]
    public async Task Steps_CanBeAddedChangedCompletedAndRemoved()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Plan trip", Steps: [new CreateTaskStepRequest("Pick dates")]), HttpStatusCode.Created);
        var stepId = Guid.CreateVersion7();

        var withStep = await alice.Client.PostAsync<TaskItemResponse>($"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Book hotel", stepId));
        var repeated = await alice.Client.PostAsync<TaskItemResponse>($"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Book hotel", stepId));

        Assert.Equal(["Pick dates", "Book hotel"], withStep.Steps.Select(step => step.Title));
        JsonAssert.Equal(withStep, repeated);
        Assert.True(withStep.ChangeVersion > task.ChangeVersion);

        var completed = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}/steps/{stepId}", new UpdateTaskStepRequest { IsCompleted = true });
        var renamed = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}/steps/{stepId}", new UpdateTaskStepRequest { Title = "Book hostel" });

        Assert.True(completed.Steps.Single(step => step.Id == stepId).IsCompleted);
        Assert.Equal("Book hostel", renamed.Steps.Single(step => step.Id == stepId).Title);
        Assert.True(renamed.Steps.Single(step => step.Id == stepId).IsCompleted);
        Assert.False(renamed.IsCompleted);

        var withoutStep = await alice.Client.SendAsync<TaskItemResponse>(HttpMethod.Delete, $"{TasksPath}/{task.Id}/steps/{stepId}");

        Assert.Equal(["Pick dates"], withoutStep.Steps.Select(step => step.Title));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}/steps/{stepId}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}/steps/{stepId}", new UpdateTaskStepRequest { IsCompleted = false }, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest(""), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_ListId_MovesTheTaskToAnotherOwnList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var work = await alice.Client.PostAsync<TaskListResponse>("/api/lists", new CreateTaskListRequest("Work"), HttpStatusCode.Created);
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Send invoice"), HttpStatusCode.Created);

        var moved = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { ListId = work.Id });

        Assert.Equal(work.Id, moved.ListId);
        Assert.Equal([task.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"{TasksPath}?view={work.Id}")).Select(item => item.Id));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { ListId = Guid.CreateVersion7() }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_SoftDeletesTheTask()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Temporary"), HttpStatusCode.Created);

        await alice.Client.DeleteAsync($"{TasksPath}/{task.Id}");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Back" }, HttpStatusCode.NotFound);
        var tombstone = await server.FindRawAsync(server.Database.Tasks, task.Id);
        Assert.True(tombstone.IsDeleted);
        Assert.True(tombstone.ChangeVersion > task.ChangeVersion);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
    }
}
