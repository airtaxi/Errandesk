using System.Net;
using System.Text.Json;
using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Tests.Infrastructure;
using static Errandesk.Server.Tests.Sharing.SharingTestHelpers;

namespace Errandesk.Server.Tests.Sharing;

public sealed class UserEnumerationTests(MongoDbContainerFixture mongoDb)
{
    [Fact]
    public async Task Share_MatchesOnlyTheExactUserName_AndAnswersEveryOtherNameIdentically()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        await server.RegisterInvitedAsync(alice, "bob.smith");
        var list = await CreateListAsync(alice, "Groceries");

        var unknown = await ShareExpectingNotFoundAsync(alice, list.Id, "nobody-here");
        string[] nearMisses = ["bob", "bob.smit", "bob.smith2", "ob.smith", "bob.*", "^bob", "bob smith", "bob.smith\u0000"];
        foreach (var nearMiss in nearMisses) Assert.Equal(unknown, await ShareExpectingNotFoundAsync(alice, list.Id, nearMiss));
        Assert.Contains("user", unknown);

        Assert.Empty((await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{list.Id}")).Members!);
        var shared = await ShareAsync(alice, list.Id, "Bob.Smith");
        Assert.Equal("bob.smith", Assert.Single(shared.Members!).UserName);
    }

    [Fact]
    public async Task Share_ByUsersWhoMayNotShare_NeverRevealsWhetherANameExists()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var dave = await server.RegisterInvitedAsync(alice, "dave");
        await server.RegisterInvitedAsync(alice, "carol");
        var list = await CreateListAsync(alice, "Trip");
        await ShareAsync(alice, list.Id, "bob");

        var memberExisting = await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("carol"), HttpStatusCode.Forbidden);
        var memberUnknown = await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("zelda"), HttpStatusCode.Forbidden);
        Assert.Equal(Describe(memberExisting), Describe(memberUnknown));

        var strangerExisting = await dave.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("carol"), HttpStatusCode.NotFound);
        var strangerUnknown = await dave.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(list.Id), new AddTaskListMemberRequest("zelda"), HttpStatusCode.NotFound);
        Assert.Equal(Describe(strangerExisting), Describe(strangerUnknown));
        Assert.Contains("list", strangerExisting.GetProperty("detail").GetString());

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, "/api/admin/users", null, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListResponses_ExposeOnlyTheIdNameAndDisplayNameOfMembers()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Asia/Seoul");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var list = await CreateListAsync(alice, "Family");
        await ShareAsync(alice, list.Id, "bob");

        var json = await bob.Client.SendExpectingAsync(HttpMethod.Get, $"{ListsPath}/{list.Id}", null, HttpStatusCode.OK);

        using var document = JsonDocument.Parse(json);
        string[] memberProperties = ["userId", "userName", "displayName", "addedAt"];
        Assert.Equal(memberProperties, document.RootElement.GetProperty("owner").EnumerateObject().Select(property => property.Name));
        Assert.Equal(memberProperties, document.RootElement.GetProperty("members")[0].EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("Asia/Seoul", json);
        Assert.DoesNotContain("isAdministrator", json);
    }

    [Fact]
    public async Task Share_IsRateLimitedPerUser()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var alicesList = await CreateListAsync(alice, "Groceries");
        var bobsList = await CreateListAsync(bob, "Garage");

        for (var attempt = 0; attempt < ListSharingRateLimiting.PermitLimit; attempt++) await ShareExpectingNotFoundAsync(alice, alicesList.Id, $"guess{attempt}");

        using var limited = await alice.Client.SendAsync(HttpMethod.Post, MembersPath(alicesList.Id), new AddTaskListMemberRequest("bob"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(ApiClient.ProblemMediaType, limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.RetryAfter is not null);
        Assert.Contains("share", await limited.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty((await alice.Client.GetAsync<TaskListResponse>($"{ListsPath}/{alicesList.Id}")).Members!);

        Assert.Equal("alice", Assert.Single((await ShareAsync(bob, bobsList.Id, "alice")).Members!).UserName);
    }

    private static async Task<string> ShareExpectingNotFoundAsync(TestUser owner, Guid listId, string userName) => Describe(await owner.Client.SendExpectingProblemAsync(HttpMethod.Post, MembersPath(listId), new AddTaskListMemberRequest(userName), HttpStatusCode.NotFound));

    /// <summary>The parts of a problem response that do not vary per request.</summary>
    private static string Describe(JsonElement problem) => string.Join("|", new[] { "type", "title", "status", "detail" }.Select(name => problem.TryGetProperty(name, out var value) ? value.ToString() : ""));
}
