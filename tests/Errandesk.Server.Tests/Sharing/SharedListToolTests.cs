using System.Net;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MongoDB.Driver;
using static Errandesk.Server.Tests.Sharing.SharingTestHelpers;

namespace Errandesk.Server.Tests.Sharing;

public sealed class SharedListToolTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task AssistantTools_OfAMember_ReadAndChangeSharedTasksButOnlyTheOwnerDeletesTheList()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Groceries");
        var milk = await CreateTaskAsync(alice, list.Id, "Buy milk");
        await ShareAsync(alice, list.Id, "bob");
        var toolset = CreateToolset(server, bob.Id);
        var cancellationToken = TestContext.Current.CancellationToken;

        var lists = Assert.IsAssignableFrom<IReadOnlyList<TaskListResponse>>(await toolset.ListTaskListsAsync(cancellationToken));
        var sharedList = Assert.Single(lists, visible => visible.Id == list.Id);
        Assert.Equal("alice", sharedList.Owner!.UserName);
        var found = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.SearchTasksAsync("milk", cancellationToken: cancellationToken));
        Assert.Equal(milk.Id, Assert.Single(found).Id);

        var updated = Assert.IsType<TaskItemResponse>(await toolset.UpdateTaskAsync(milk.Id, title: "Buy oat milk", isImportant: true, cancellationToken: cancellationToken));
        Assert.Equal("Buy oat milk", updated.Title);
        Assert.Equal("Buy oat milk", (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).Title);
        var updateAction = await server.Database.AgentActions.Find(action => action.ToolName == AgentToolNames.UpdateTask).SingleAsync(cancellationToken);
        await bob.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, $"/api/agent-actions/{updateAction.Id}/undo");
        Assert.Equal("Buy milk", (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).Title);
        var created = Assert.IsType<TaskItemResponse>(await toolset.CreateTaskAsync("Buy bread", listId: list.Id, addToMyDay: true, cancellationToken: cancellationToken));
        Assert.True(created.IsInMyDay);
        Assert.False((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{created.Id}")).IsInMyDay);
        var reminder = Assert.IsType<AgentReminderResult>(await toolset.SetReminderAsync(milk.Id, "2031-03-11T09:00", cancellationToken: cancellationToken));
        Assert.Equal(milk.Id, reminder.TaskId);
        Assert.Equal(bob.Id, (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).ReminderSetByUserId);
        var myDay = Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.GetMyDayAsync(cancellationToken));
        Assert.Equal(created.Id, Assert.Single(myDay).Id);

        var deleteListError = Assert.IsType<AgentToolError>(await toolset.DeleteTaskListAsync(list.Id, cancellationToken));
        Assert.Contains("owner", deleteListError.Error);
        Assert.NotNull(await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{list.Id}"));

        Assert.IsType<AgentDeletionResult>(await toolset.DeleteTaskAsync(created.Id, cancellationToken));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{created.Id}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssistantTools_OfARemovedMember_NoLongerSeeTheSharedTasks()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Groceries");
        var milk = await CreateTaskAsync(alice, list.Id, "Buy milk");
        await ShareAsync(alice, list.Id, "bob");
        var toolset = CreateToolset(server, bob.Id);
        var cancellationToken = TestContext.Current.CancellationToken;
        var completedTask = Assert.IsType<TaskItemResponse>(await toolset.CompleteTaskAsync(milk.Id, cancellationToken: cancellationToken));
        Assert.True(completedTask.IsCompleted);

        await RemoveMemberAsync(bob, list.Id, bob.Id);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<TaskItemResponse>>(await toolset.SearchTasksAsync("milk", includeCompleted: true, cancellationToken: cancellationToken)));
        Assert.DoesNotContain(Assert.IsAssignableFrom<IReadOnlyList<TaskListResponse>>(await toolset.ListTaskListsAsync(cancellationToken)), visible => visible.Id == list.Id);
        Assert.Contains("not found", Assert.IsType<AgentToolError>(await toolset.UpdateTaskAsync(milk.Id, title: "Mine now", cancellationToken: cancellationToken)).Error);
        Assert.Contains("not found", Assert.IsType<AgentToolError>(await toolset.CreateTaskAsync("Sneak in", listId: list.Id, cancellationToken: cancellationToken)).Error);
        Assert.Contains("not found", Assert.IsType<AgentToolError>(await toolset.DeleteTaskAsync(milk.Id, cancellationToken)).Error);

        var action = await server.Database.AgentActions.Find(stored => stored.OwnerUserId == bob.Id).SingleAsync(cancellationToken);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, $"/api/agent-actions/{action.Id}/undo", null, HttpStatusCode.Conflict);
        Assert.True((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).IsCompleted);
    }

    [Fact]
    public async Task McpTools_FollowTheSameOwnerOrMemberRule()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var dave = await server.RegisterInvitedAsync(alice, "dave");
        var list = await CreateListAsync(alice, "Groceries");
        var milk = await CreateTaskAsync(alice, list.Id, "Buy milk");
        await ShareAsync(alice, list.Id, "bob");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bobClient = await ConnectAsync(server, await CreateTokenAsync(bob));
        await using var daveClient = await ConnectAsync(server, await CreateTokenAsync(dave));

        Assert.Contains(milk.Id.ToString(), GetText(await bobClient.CallToolAsync(AgentToolNames.SearchTasks, new Dictionary<string, object?> { ["query"] = "milk" }, cancellationToken: cancellationToken)));
        var completed = await bobClient.CallToolAsync(AgentToolNames.CompleteTask, new Dictionary<string, object?> { ["taskId"] = milk.Id }, cancellationToken: cancellationToken);
        Assert.NotEqual(true, completed.IsError);
        Assert.True((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).IsCompleted);
        var created = await bobClient.CallToolAsync(AgentToolNames.CreateTask, new Dictionary<string, object?> { ["title"] = "Buy bread", ["listId"] = list.Id }, cancellationToken: cancellationToken);
        using var createdJson = JsonDocument.Parse(GetText(created));
        Assert.Equal(list.Id, createdJson.RootElement.GetProperty("listId").GetGuid());

        Assert.DoesNotContain(milk.Id.ToString(), GetText(await daveClient.CallToolAsync(AgentToolNames.SearchTasks, new Dictionary<string, object?> { ["query"] = "milk", ["includeCompleted"] = true }, cancellationToken: cancellationToken)));
        Assert.True((await daveClient.CallToolAsync(AgentToolNames.UpdateTask, new Dictionary<string, object?> { ["taskId"] = milk.Id, ["title"] = "Mine" }, cancellationToken: cancellationToken)).IsError);
        var deleteList = await bobClient.CallToolAsync(AgentToolNames.DeleteTaskList, new Dictionary<string, object?> { ["listId"] = list.Id }, cancellationToken: cancellationToken);
        Assert.True(deleteList.IsError);

        await RemoveMemberAsync(alice, list.Id, bob.Id);
        Assert.DoesNotContain(milk.Id.ToString(), GetText(await bobClient.CallToolAsync(AgentToolNames.SearchTasks, new Dictionary<string, object?> { ["query"] = "milk", ["includeCompleted"] = true }, cancellationToken: cancellationToken)));
        Assert.True((await bobClient.CallToolAsync(AgentToolNames.UpdateTask, new Dictionary<string, object?> { ["taskId"] = milk.Id, ["title"] = "Mine" }, cancellationToken: cancellationToken)).IsError);
        Assert.Equal("Buy milk", (await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{milk.Id}")).Title);
    }

    private static AgentToolset CreateToolset(ApiTestServer server, Guid userId) => server.Services.GetRequiredService<AgentToolsetFactory>().Create(new AgentRunContext(userId, null));

    private static async Task<string> CreateTokenAsync(TestUser user) => (await user.Client.PostAsync<CreatedPersonalAccessTokenResponse>("/api/users/me/tokens", new CreatePersonalAccessTokenRequest("MCP"), HttpStatusCode.Created)).Token;

    private static async Task<McpClient> ConnectAsync(ApiTestServer server, string token)
    {
        var httpClient = server.CreateHttpClient(token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, PersonalAccessTokenFormat.McpEndpointPath), TransportMode = HttpTransportMode.StreamableHttp }, httpClient, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string GetText(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
