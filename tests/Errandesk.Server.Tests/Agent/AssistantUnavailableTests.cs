using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Agent;

public sealed class AssistantUnavailableTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task AssistantEndpoints_WithoutAnAiModel_ReturnServiceUnavailable()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var conversationId = Guid.CreateVersion7();

        Assert.False((await server.Anonymous.GetAsync<ServerInfoResponse>("/api/server-info")).IsAiAvailable);
        var problem = await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, "/api/conversations", null, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("The server has no AI model configured.", problem.GetProperty("detail").GetString());
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, "/api/conversations", new CreateConversationRequest(), HttpStatusCode.ServiceUnavailable);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/conversations/{conversationId}", null, HttpStatusCode.ServiceUnavailable);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, $"/api/conversations/{conversationId}/messages", null, HttpStatusCode.ServiceUnavailable);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"/api/conversations/{conversationId}", null, HttpStatusCode.ServiceUnavailable);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"/api/conversations/{conversationId}/messages", new SendMessageRequest("Hello"), HttpStatusCode.ServiceUnavailable);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"/api/conversations/{conversationId}/approvals", new ApprovalDecisionsRequest([new ApprovalDecision("approval", true)]), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ChangeFeedAndUndo_WithoutAnAiModel_StayAvailable()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.GetAsync<ChangesResponse>("/api/changes?since=0");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"/api/agent-actions/{Guid.CreateVersion7()}/undo", null, HttpStatusCode.NotFound);
    }
}
