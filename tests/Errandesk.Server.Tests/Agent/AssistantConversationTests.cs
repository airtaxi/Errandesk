using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Tests.Agent;

public sealed class AssistantConversationTests(MongoDbContainerFixture mongoDb)
{
    private const string ConversationsPath = "/api/conversations";
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Conversations_CanBeCreatedListedReadAndDeleted()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var conversationId = Guid.CreateVersion7();

        Assert.True((await server.Anonymous.GetAsync<ServerInfoResponse>("/api/server-info")).IsAiAvailable);
        var created = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest("  Weekly plan ", conversationId), HttpStatusCode.Created);
        var repeated = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest("Other", conversationId), HttpStatusCode.OK);
        var untitled = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);

        Assert.Equal("Weekly plan", created.Title);
        Assert.False(created.HasPendingApprovals);
        JsonAssert.Equal(created, repeated);
        Assert.Equal("", untitled.Title);
        Assert.Equal(2, (await alice.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath)).Count);
        JsonAssert.Equal(created, await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversationId}"));
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversationId}/messages"));
        Assert.Empty(await bob.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath));
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ConversationsPath}/{conversationId}", null, HttpStatusCode.NotFound);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{ConversationsPath}/{conversationId}/messages", new SendMessageRequest("Hello"), HttpStatusCode.NotFound);

        await alice.Client.DeleteAsync($"{ConversationsPath}/{conversationId}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{ConversationsPath}/{conversationId}", null, HttpStatusCode.NotFound);
        Assert.Equal(untitled.Id, Assert.Single(await alice.Client.GetAsync<IReadOnlyList<ConversationResponse>>(ConversationsPath)).Id);
        Assert.Empty(chatClient.ReceivedMessages);
    }

    [Fact]
    public async Task Conversation_RunsToolsStreamsEventsAndPausesForApprovalBeforeDeleting()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);

        chatClient.EnqueueToolCall("call-1", AgentToolNames.CreateTask, """{"title":"Buy milk","dueDate":"2031-03-11"}""", "Adding it. ");
        chatClient.EnqueueText("Added ", "**Buy milk** for tomorrow.");
        var firstEvents = await SendMessageAsync(alice, conversation.Id, "Add buy milk for tomorrow");

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.TextDelta, ServerEventNames.ToolStarted, ServerEventNames.ToolCompleted, ServerEventNames.TextDelta, ServerEventNames.TextDelta, ServerEventNames.MessageCompleted], firstEvents.Select(serverEvent => serverEvent.EventType));
        var firstStarted = firstEvents[0].Deserialize<MessageStartedEventData>();
        Assert.Equal(conversation.Id, firstStarted.ConversationId);
        Assert.Equal(new ToolStartedEventData("call-1", AgentToolNames.CreateTask), firstEvents[2].Deserialize<ToolStartedEventData>());
        var createCompleted = firstEvents[3].Deserialize<ToolCompletedEventData>();
        Assert.True(createCompleted.Succeeded);
        Assert.Null(createCompleted.Error);
        var createActionId = Assert.NotNull(createCompleted.AgentActionId);
        var createAction = await server.FindRawAsync(server.Database.AgentActions, createActionId);
        Assert.Equal(conversation.Id, createAction.ConversationId);
        Assert.Contains("\"title\":\"Buy milk\"", createAction.ArgumentsJson, StringComparison.Ordinal);
        var firstCompleted = firstEvents[^1].Deserialize<MessageCompletedEventData>();
        Assert.Equal(new MessageCompletedEventData(conversation.Id, firstStarted.MessageId, "Adding it. \n\nAdded **Buy milk** for tomorrow.", false), firstCompleted);

        var task = Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(new DateOnly(2031, 3, 11), task.DueDate);
        var firstRequest = chatClient.ReceivedMessages[0];
        Assert.Equal(ChatRole.System, firstRequest[0].Role);
        Assert.Contains("2031-03-10T12:00", firstRequest[0].Text, StringComparison.Ordinal);
        Assert.Contains("alice", firstRequest[0].Text, StringComparison.Ordinal);
        Assert.Equal("Add buy milk for tomorrow", firstRequest[^1].Text);
        Assert.Equal(21, chatClient.ReceivedOptions[0]!.Tools!.Count);
        Assert.Contains(chatClient.ReceivedMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.CallId == "call-1" && result.Result!.ToString()!.Contains("Buy milk", StringComparison.Ordinal));
        Assert.Equal("Add buy milk for tomorrow", (await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversation.Id}")).Title);

        chatClient.EnqueueToolCall("call-2", AgentToolNames.DeleteTask, $$"""{"taskId":"{{task.Id}}"}""");
        var secondEvents = await SendMessageAsync(alice, conversation.Id, "Delete it");

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.ApprovalRequested, ServerEventNames.MessageCompleted], secondEvents.Select(serverEvent => serverEvent.EventType));
        var approval = secondEvents[1].Deserialize<ApprovalRequestResponse>();
        Assert.Equal(AgentToolNames.DeleteTask, approval.ToolName);
        Assert.Equal(EntityTypes.Task, approval.EntityType);
        Assert.Equal(task.Id, approval.EntityId);
        Assert.Equal("Buy milk", approval.EntityTitle);
        Assert.Equal(ApprovalStatuses.Pending, approval.Status);
        Assert.True(secondEvents[^1].Deserialize<MessageCompletedEventData>().HasPendingApprovals);
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.True((await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversation.Id}")).HasPendingApprovals);
        var secondRequest = chatClient.ReceivedMessages[2];
        Assert.Contains(secondRequest, message => message.Role == ChatRole.User && message.Text == "Add buy milk for tomorrow");
        Assert.Contains(secondRequest.SelectMany(message => message.Contents).OfType<FunctionCallContent>(), call => call.CallId == "call-1");
        Assert.Contains(secondRequest.SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.CallId == "call-1");
        Assert.Contains(secondRequest, message => message.Role == ChatRole.Assistant && message.Text.Contains("for tomorrow", StringComparison.Ordinal));

        chatClient.EnqueueText("Deleted **Buy milk**.");
        var thirdEvents = await alice.Client.PostForEventsAsync($"{ConversationsPath}/{conversation.Id}/approvals", new ApprovalDecisionsRequest([new ApprovalDecision(approval.ApprovalId, true)]));

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.ToolStarted, ServerEventNames.ToolCompleted, ServerEventNames.TextDelta, ServerEventNames.MessageCompleted], thirdEvents.Select(serverEvent => serverEvent.EventType));
        var deleteCompleted = thirdEvents[2].Deserialize<ToolCompletedEventData>();
        Assert.Equal(AgentToolNames.DeleteTask, deleteCompleted.ToolName);
        Assert.True(deleteCompleted.Succeeded);
        var deleteActionId = Assert.NotNull(deleteCompleted.AgentActionId);
        var thirdCompleted = thirdEvents[^1].Deserialize<MessageCompletedEventData>();
        Assert.Equal("Deleted **Buy milk**.", thirdCompleted.Text);
        Assert.False(thirdCompleted.HasPendingApprovals);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
        Assert.False((await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversation.Id}")).HasPendingApprovals);
        var thirdRequestContents = chatClient.ReceivedMessages[3].SelectMany(message => message.Contents).ToList();
        Assert.Contains(thirdRequestContents.OfType<FunctionResultContent>(), result => result.CallId == "call-2");
        Assert.Empty(thirdRequestContents.OfType<ToolApprovalRequestContent>());
        Assert.Empty(thirdRequestContents.OfType<ToolApprovalResponseContent>());

        await alice.Client.SendAsync<AgentActionResponse>(HttpMethod.Post, $"/api/agent-actions/{deleteActionId}/undo");
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");

        var history = await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal([ConversationRoles.User, ConversationRoles.Assistant, ConversationRoles.User, ConversationRoles.Assistant, ConversationRoles.Assistant], history.Select(message => message.Role));
        Assert.Equal("Add buy milk for tomorrow", history[0].Text);
        Assert.Equal(firstStarted.MessageId, history[1].Id);
        Assert.Equal(firstCompleted.Text, history[1].Text);
        Assert.Equal(new ToolActivityResponse("call-1", AgentToolNames.CreateTask, true, createActionId, false), Assert.Single(history[1].ToolActivities));
        Assert.Equal(approval with { Status = ApprovalStatuses.Approved }, Assert.Single(history[3].Approvals));
        Assert.Equal(thirdEvents[0].Deserialize<MessageStartedEventData>().MessageId, history[4].Id);
        Assert.Equal(new ToolActivityResponse("call-2", AgentToolNames.DeleteTask, true, deleteActionId, true), Assert.Single(history[4].ToolActivities));
    }

    [Fact]
    public async Task DeniedApproval_DoesNotRunTheToolAndTellsTheModel()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Quarterly report"), HttpStatusCode.Created);
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);
        chatClient.EnqueueToolCall("call-1", AgentToolNames.DeleteTask, $$"""{"taskId":"{{task.Id}}"}""");
        var approval = (await SendMessageAsync(alice, conversation.Id, "Delete the report task"))[1].Deserialize<ApprovalRequestResponse>();

        chatClient.EnqueueText("Okay, I kept it.");
        var events = await alice.Client.PostForEventsAsync($"{ConversationsPath}/{conversation.Id}/approvals", new ApprovalDecisionsRequest([new ApprovalDecision(approval.ApprovalId, false)]));

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.TextDelta, ServerEventNames.MessageCompleted], events.Select(serverEvent => serverEvent.EventType));
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        var rejection = Assert.Single(chatClient.ReceivedMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.CallId == "call-1");
        Assert.Contains("denied", rejection.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
        var history = await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal(ApprovalStatuses.Denied, Assert.Single(history[1].Approvals).Status);
        Assert.Empty(history.SelectMany(message => message.ToolActivities));
    }

    [Fact]
    public async Task NewMessage_WhileAnApprovalIsPending_DeniesItAndContinues()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var note = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Recipes"), HttpStatusCode.Created);
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);
        chatClient.EnqueueToolCall("call-1", AgentToolNames.DeleteNote, $$"""{"noteId":"{{note.Id}}"}""");
        await SendMessageAsync(alice, conversation.Id, "Delete my recipes note");

        chatClient.EnqueueText("Fine, nothing was deleted.");
        var events = await SendMessageAsync(alice, conversation.Id, "Never mind");

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.TextDelta, ServerEventNames.MessageCompleted], events.Select(serverEvent => serverEvent.EventType));
        await alice.Client.GetAsync<NoteResponse>($"/api/notes/{note.Id}");
        var request = chatClient.ReceivedMessages[1];
        Assert.Contains(request.SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.CallId == "call-1");
        Assert.Equal("Never mind", request[^1].Text);
        var history = await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal(ApprovalStatuses.Denied, Assert.Single(history.SelectMany(message => message.Approvals)).Status);
        Assert.False((await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversation.Id}")).HasPendingApprovals);
    }

    [Fact]
    public async Task InvalidRequests_AreRejectedBeforeStreaming()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Report"), HttpStatusCode.Created);
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);
        var approvalsPath = $"{ConversationsPath}/{conversation.Id}/approvals";

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{ConversationsPath}/{conversation.Id}/messages", new SendMessageRequest("   "), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{ConversationsPath}/{Guid.CreateVersion7()}/messages", new SendMessageRequest("Hello"), HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, approvalsPath, new ApprovalDecisionsRequest([new ApprovalDecision("missing", true)]), HttpStatusCode.Conflict);

        chatClient.EnqueueToolCall("call-1", AgentToolNames.DeleteTask, $$"""{"taskId":"{{task.Id}}"}""");
        var approval = (await SendMessageAsync(alice, conversation.Id, "Delete the report"))[1].Deserialize<ApprovalRequestResponse>();

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, approvalsPath, new ApprovalDecisionsRequest([]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, approvalsPath, new ApprovalDecisionsRequest([new ApprovalDecision("missing", true)]), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, approvalsPath, new ApprovalDecisionsRequest([new ApprovalDecision(approval.ApprovalId, true), new ApprovalDecision(approval.ApprovalId, false)]), HttpStatusCode.BadRequest);
        await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.True((await alice.Client.GetAsync<ConversationResponse>($"{ConversationsPath}/{conversation.Id}")).HasPendingApprovals);
        Assert.Equal(0, chatClient.PendingResponseCount);
    }

    [Fact]
    public async Task ModelFailure_StreamsAnErrorAndReleasesTheConversation()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);
        chatClient.Enqueue(_ => throw new InvalidOperationException("The model is unavailable."));

        var failedEvents = await SendMessageAsync(alice, conversation.Id, "Hello");

        Assert.Equal([ServerEventNames.MessageStarted, ServerEventNames.Error], failedEvents.Select(serverEvent => serverEvent.EventType));
        Assert.Equal("The assistant could not finish the reply.", failedEvents[1].Deserialize<StreamErrorEventData>().Message);

        chatClient.EnqueueText("Hi there.");
        var events = await SendMessageAsync(alice, conversation.Id, "Hello again");
        Assert.Equal(ServerEventNames.MessageCompleted, events[^1].EventType);
        var history = await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages");
        Assert.Equal(["Hello", "Hello again", "Hi there."], history.Select(message => message.Text));
    }

    [Fact]
    public async Task LongConversation_SendsOnlyABoundedRecentHistory()
    {
        var chatClient = new ScriptedChatClient();
        await using var server = await ApiTestServer.StartAsync(mongoDb, chatClient: chatClient);
        var alice = await server.RegisterAsync("alice");
        var conversation = await alice.Client.PostAsync<ConversationResponse>(ConversationsPath, new CreateConversationRequest(), HttpStatusCode.Created);
        const int TurnCount = 25;
        for (var turn = 1; turn <= TurnCount; turn++)
        {
            chatClient.EnqueueText($"Reply {turn}");
            await SendMessageAsync(alice, conversation.Id, $"Message {turn}");
        }

        chatClient.EnqueueText("Last reply");
        await SendMessageAsync(alice, conversation.Id, "Last message");

        var request = chatClient.ReceivedMessages[^1];
        Assert.Equal(AssistantConversationRunner.HistoryMessageLimit + 2, request.Count);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal("Message 6", request[1].Text);
        Assert.Equal("Reply 25", request[^2].Text);
        Assert.Equal("Last message", request[^1].Text);
        Assert.Equal((TurnCount + 1) * 2, (await alice.Client.GetAsync<IReadOnlyList<ConversationMessageResponse>>($"{ConversationsPath}/{conversation.Id}/messages")).Count);
    }

    private static Task<IReadOnlyList<ReceivedServerEvent>> SendMessageAsync(TestUser user, Guid conversationId, string text) => user.Client.PostForEventsAsync($"{ConversationsPath}/{conversationId}/messages", new SendMessageRequest(text));
}
