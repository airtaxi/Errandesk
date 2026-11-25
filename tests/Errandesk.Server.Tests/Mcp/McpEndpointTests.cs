using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Mcp;

public sealed class McpEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string TokensPath = "/api/users/me/tokens";

    private static readonly string[] s_toolNames =
    [
        AgentToolNames.ListTaskLists, AgentToolNames.CreateTaskList, AgentToolNames.SearchTasks, AgentToolNames.GetMyDay, AgentToolNames.GetOverdueTasks, AgentToolNames.CreateTask, AgentToolNames.UpdateTask,
        AgentToolNames.CompleteTask, AgentToolNames.AddStep, AgentToolNames.CompleteStep, AgentToolNames.AddToMyDay, AgentToolNames.SearchNotes, AgentToolNames.ReadNote, AgentToolNames.CreateNote, AgentToolNames.UpdateNote,
        AgentToolNames.EditNoteContent, AgentToolNames.LinkNoteToTask, AgentToolNames.GetCurrentTime, AgentToolNames.SetReminder, AgentToolNames.SetRecurrence, AgentToolNames.DeleteTask, AgentToolNames.DeleteTaskList, AgentToolNames.DeleteNote,
    ];

    [Fact]
    public async Task ListTools_ReturnsTheAssistantToolsetWithAnnotations()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        await using var client = await ConnectAsync(server, await CreateTokenAsync(alice));

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("errandesk", client.ServerInfo?.Name);
        Assert.False(string.IsNullOrWhiteSpace(client.ServerInstructions));
        Assert.Equal(s_toolNames.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        foreach (var tool in tools)
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(AgentToolset.RequiresApproval(tool.Name), annotations.DestructiveHint);
            Assert.Equal(AgentToolset.IsReadOnly(tool.Name), annotations.ReadOnlyHint);
            Assert.False(annotations.OpenWorldHint);
        }

        Assert.True(Find(tools, AgentToolNames.DeleteTask).ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(Find(tools, AgentToolNames.DeleteTaskList).ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(Find(tools, AgentToolNames.DeleteNote).ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(Find(tools, AgentToolNames.SearchTasks).ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(Find(tools, AgentToolNames.CreateTask).ProtocolTool.Annotations!.DestructiveHint);
        Assert.Contains("\"title\"", Find(tools, AgentToolNames.CreateTask).ProtocolTool.InputSchema.GetRawText());
    }

    [Fact]
    public async Task CallTool_ChangesTheOwnersDataThroughTheSharedServicesAndRecordsUndoableActions()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        await using var client = await ConnectAsync(server, await CreateTokenAsync(alice));

        var created = await client.CallToolAsync(AgentToolNames.CreateTask, new Dictionary<string, object?> { ["title"] = "Buy milk", ["dueDate"] = "2031-03-12", ["isImportant"] = true }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, created.IsError);
        using var createdJson = JsonDocument.Parse(GetText(created));
        var taskId = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Buy milk", createdJson.RootElement.GetProperty("title").GetString());

        var task = await alice.Client.GetAsync<TaskItemResponse>($"/api/tasks/{taskId}");
        Assert.Equal(new DateOnly(2031, 3, 12), task.DueDate);
        Assert.True(task.IsImportant);

        var action = await server.Database.AgentActions.Find(agentAction => agentAction.EntityId == taskId).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AgentToolNames.CreateTask, action.ToolName);
        Assert.Equal(alice.Id, action.OwnerUserId);
        Assert.Null(action.ConversationId);
        Assert.Contains("Buy milk", action.ArgumentsJson);

        var searched = await client.CallToolAsync(AgentToolNames.SearchTasks, new Dictionary<string, object?> { ["query"] = "milk" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(taskId.ToString(), GetText(searched));

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, $"/api/agent-actions/{action.Id}/undo");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks/{taskId}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CallTool_RunsDestructiveToolsAndReportsDomainErrorsAsToolErrors()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Old task"), HttpStatusCode.Created);
        await using var client = await ConnectAsync(server, await CreateTokenAsync(alice));

        var missing = await client.CallToolAsync(AgentToolNames.UpdateTask, new Dictionary<string, object?> { ["taskId"] = Guid.CreateVersion7(), ["title"] = "Renamed" }, cancellationToken: TestContext.Current.CancellationToken);
        var invalid = await client.CallToolAsync(AgentToolNames.SetRecurrence, new Dictionary<string, object?> { ["taskId"] = task.Id, ["frequency"] = "hourly" }, cancellationToken: TestContext.Current.CancellationToken);
        var deleted = await client.CallToolAsync(AgentToolNames.DeleteTask, new Dictionary<string, object?> { ["taskId"] = task.Id }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(missing.IsError);
        Assert.Contains("task was not found", GetText(missing));
        Assert.True(invalid.IsError);
        Assert.Contains("frequency", GetText(invalid));
        Assert.NotEqual(true, deleted.IsError);
        Assert.Contains("Old task", GetText(deleted));
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks/{task.Id}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CallTool_SeesOnlyTheTokenOwnersData()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob");
        var alicesTask = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Alice's secret"), HttpStatusCode.Created);
        await using var bobsClient = await ConnectAsync(server, await CreateTokenAsync(bob));

        var searched = await bobsClient.CallToolAsync(AgentToolNames.SearchTasks, new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
        var deleted = await bobsClient.CallToolAsync(AgentToolNames.DeleteTask, new Dictionary<string, object?> { ["taskId"] = alicesTask.Id }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await bobsClient.CallToolAsync(AgentToolNames.CreateTask, new Dictionary<string, object?> { ["title"] = "Bob's task" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("[]", GetText(searched));
        Assert.True(deleted.IsError);
        Assert.NotEqual(true, created.IsError);
        await alice.Client.GetAsync<TaskItemResponse>($"/api/tasks/{alicesTask.Id}");
        Assert.DoesNotContain(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks"), item => item.Title == "Bob's task");
        Assert.Contains(await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks"), item => item.Title == "Bob's task");
    }

    [Fact]
    public async Task Endpoint_RequiresAnActivePersonalAccessToken()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var created = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("MCP"), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.OK, await PostListToolsAsync(server, created.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostListToolsAsync(server, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostListToolsAsync(server, alice.Authentication.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostListToolsAsync(server, PersonalAccessTokenFormat.Prefix + new string('x', 40)));

        await alice.Client.DeleteAsync($"{TokensPath}/{created.PersonalAccessToken.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, await PostListToolsAsync(server, created.Token));
    }

    private static async Task<string> CreateTokenAsync(TestUser user) => (await user.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("MCP"), HttpStatusCode.Created)).Token;

    private static async Task<McpClient> ConnectAsync(ApiTestServer server, string token)
    {
        var httpClient = server.CreateHttpClient(token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, PersonalAccessTokenFormat.McpEndpointPath), TransportMode = HttpTransportMode.StreamableHttp }, httpClient, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Sends a raw JSON-RPC <c>tools/list</c> request to the endpoint and returns the status code.</summary>
    private static async Task<HttpStatusCode> PostListToolsAsync(ApiTestServer server, string? bearerToken)
    {
        using var httpClient = server.CreateHttpClient(bearerToken ?? "");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{PersonalAccessTokenFormat.McpEndpointPath}")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static McpClientTool Find(IEnumerable<McpClientTool> tools, string name) => tools.Single(tool => tool.Name == name);

    private static string GetText(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
