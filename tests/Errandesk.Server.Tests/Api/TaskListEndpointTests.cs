using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Tests.Infrastructure;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class TaskListEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string ListsPath = "/api/lists";

    [Fact]
    public async Task Lists_SupportCreateReadUpdateAndDelete()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var groceries = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Groceries"), HttpStatusCode.Created);
        var work = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("  Work  "), HttpStatusCode.Created);

        Assert.Equal("Work", work.Name);
        Assert.False(work.IsDefault);
        Assert.True(work.SortOrder > groceries.SortOrder);
        Assert.Equal(["Tasks", "Groceries", "Work"], (await alice.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath)).Select(list => list.Name));
        JsonAssert.Equal(groceries, await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{groceries.Id}"));

        var renamed = await alice.Client.PatchAsync<TaskListResponse>($"{ListsPath}/{groceries.Id}", new UpdateTaskListRequest { Name = "Shopping" });

        Assert.Equal("Shopping", renamed.Name);
        Assert.Equal(groceries.SortOrder, renamed.SortOrder);
        Assert.True(renamed.ChangeVersion > work.ChangeVersion);
        Assert.True(renamed.UpdatedAt >= groceries.UpdatedAt);

        await alice.Client.DeleteAsync($"{ListsPath}/{groceries.Id}");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ListsPath}/{groceries.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{ListsPath}/{groceries.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal(["Tasks", "Work"], (await alice.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath)).Select(list => list.Name));
    }

    [Fact]
    public async Task DefaultList_CannotBeRenamedOrDeleted()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var defaultList = await server.GetDefaultListAsync(alice);

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, $"{ListsPath}/{defaultList.Id}", new UpdateTaskListRequest { Name = "Inbox" }, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{ListsPath}/{defaultList.Id}", null, HttpStatusCode.BadRequest);
        var reordered = await alice.Client.PatchAsync<TaskListResponse>($"{ListsPath}/{defaultList.Id}", new UpdateTaskListRequest { SortOrder = -5 });

        Assert.Equal("Tasks", reordered.Name);
        Assert.Equal(-5, reordered.SortOrder);
    }

    [Fact]
    public async Task Create_WithClientId_IsIdempotent()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var listId = Guid.CreateVersion7();

        var created = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Groceries", listId), HttpStatusCode.Created);
        var repeated = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Something else", listId), HttpStatusCode.OK);

        Assert.Equal(listId, created.Id);
        JsonAssert.Equal(created, repeated);
        Assert.Equal(2, (await alice.Client.GetAsync<IReadOnlyList<TaskListResponse>>(ListsPath)).Count);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, ListsPath, new CreateTaskListRequest("Mine", listId), HttpStatusCode.Conflict);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, ListsPath, new CreateTaskListRequest("Empty", Guid.Empty), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WithStaleExpectedChangeVersion_ReturnsConflictWithTheCurrentList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Groceries"), HttpStatusCode.Created);
        var renamed = await alice.Client.PatchAsync<TaskListResponse>($"{ListsPath}/{list.Id}", new UpdateTaskListRequest { Name = "Shopping", ExpectedChangeVersion = list.ChangeVersion });

        var conflictBody = await alice.Client.SendExpectingAsync(HttpMethod.Patch, $"{ListsPath}/{list.Id}", new UpdateTaskListRequest { Name = "Errands", ExpectedChangeVersion = list.ChangeVersion }, HttpStatusCode.Conflict);

        JsonAssert.Equal(renamed, ApiClient.DeserializeJson<TaskListResponse>(conflictBody));
        Assert.Equal("Shopping", (await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{list.Id}")).Name);
    }

    [Fact]
    public async Task Delete_SoftDeletesTheListAndItsTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var list = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Groceries"), HttpStatusCode.Created);
        var milk = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Milk", ListId: list.Id, IsImportant: true), HttpStatusCode.Created);
        var bread = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Bread", ListId: list.Id), HttpStatusCode.Created);
        var unrelated = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Call mom"), HttpStatusCode.Created);

        await alice.Client.DeleteAsync($"{ListsPath}/{list.Id}");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks/{milk.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks?view={list.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal([unrelated.Id], (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks")).Select(task => task.Id));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>($"/api/tasks?view={TaskViews.Important}"));

        var listTombstone = await server.FindRawAsync(server.Database.TaskLists, list.Id);
        Assert.True(listTombstone.IsDeleted);
        Assert.Equal(ApiTestServer.DefaultStartTime.UtcDateTime, listTombstone.DeletedAt);
        foreach (var taskId in new[] { milk.Id, bread.Id })
        {
            var taskTombstone = await server.FindRawAsync(server.Database.Tasks, taskId);
            Assert.True(taskTombstone.IsDeleted);
            Assert.NotNull(taskTombstone.DeletedAt);
            Assert.True(taskTombstone.ChangeVersion > bread.ChangeVersion);
        }
    }

    [Fact]
    public async Task ChangeVersions_ComeFromOneGlobalCounter()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");

        var aliceList = await alice.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Alice"), HttpStatusCode.Created);
        var bobList = await bob.Client.PostAsync<TaskListResponse>(ListsPath, new CreateTaskListRequest("Bob"), HttpStatusCode.Created);
        var aliceTask = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Task", ListId: aliceList.Id), HttpStatusCode.Created);

        Assert.True(bobList.ChangeVersion > aliceList.ChangeVersion);
        Assert.True(aliceTask.ChangeVersion > bobList.ChangeVersion);
        var counter = await server.Database.ChangeCounters.Find(counter => counter.Id == ChangeVersionGenerator.GlobalCounterId).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(counter.Value >= aliceTask.ChangeVersion);
        Assert.Equal(1, await server.Database.ChangeCounters.CountDocumentsAsync(FilterDefinition<ChangeCounterDocument>.Empty, cancellationToken: TestContext.Current.CancellationToken));
    }
}
