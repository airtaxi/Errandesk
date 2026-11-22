using System.Net;
using System.Security.Cryptography;
using System.Text;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class PersonalAccessTokenTests(MongoDbContainerFixture mongoDb)
{
    private const string TokensPath = "/api/users/me/tokens";

    [Fact]
    public async Task Create_ReturnsTheTokenOnceAndStoresOnlyItsHash()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var created = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("  Desktop app  "), HttpStatusCode.Created);

        Assert.Matches("^edk_[A-Za-z0-9]{40}$", created.Token);
        Assert.Equal("Desktop app", created.PersonalAccessToken.Name);
        Assert.Equal(created.Token[..10], created.PersonalAccessToken.DisplayPrefix);
        Assert.Equal(ApiTestServer.DefaultStartTime, created.PersonalAccessToken.CreatedAt);
        Assert.Null(created.PersonalAccessToken.LastUsedAt);
        Assert.Equal([created.PersonalAccessToken], await alice.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath));
        Assert.DoesNotContain(created.Token, await alice.Client.SendExpectingAsync(HttpMethod.Get, TokensPath, null, HttpStatusCode.OK));

        var stored = await server.FindRawAsync(server.Database.ApiTokens, created.PersonalAccessToken.Id);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Token))), stored.TokenHash);
        Assert.Equal(alice.Id, stored.OwnerUserId);
        var rawDocument = await server.Database.ApiTokens.Database.GetCollection<BsonDocument>("api_tokens").Find(new BsonDocument("_id", new BsonBinaryData(created.PersonalAccessToken.Id, GuidRepresentation.Standard))).SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(created.Token, rawDocument.ToJson());
    }

    [Fact]
    public async Task Create_GeneratesDistinctTokensAndValidatesTheName()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var first = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Laptop"), HttpStatusCode.Created);
        var second = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Laptop"), HttpStatusCode.Created);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TokensPath, new CreatePersonalAccessTokenRequest("   "), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, TokensPath, new CreatePersonalAccessTokenRequest(new string('a', 101)), HttpStatusCode.BadRequest);

        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal([second.PersonalAccessToken.Id, first.PersonalAccessToken.Id], (await alice.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath)).Select(token => token.Id));
    }

    [Fact]
    public async Task Token_AuthenticatesApiRequestsAndRecordsWhenItWasUsed()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var created = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Script"), HttpStatusCode.Created);
        var tokenClient = server.CreateClient(created.Token);

        var profile = await tokenClient.GetAsync<UserResponse>("/api/users/me");
        var task = await tokenClient.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Created with a token"), HttpStatusCode.Created);
        Assert.Equal("alice", profile.UserName);
        Assert.Contains(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>("/api/tasks"), item => item.Id == task.Id);
        Assert.Equal(ApiTestServer.DefaultStartTime.UtcDateTime, (await server.FindRawAsync(server.Database.ApiTokens, created.PersonalAccessToken.Id)).LastUsedAt);

        server.TimeProvider.Advance(TimeSpan.FromSeconds(30));
        await tokenClient.GetAsync<UserResponse>("/api/users/me");
        Assert.Equal(ApiTestServer.DefaultStartTime.UtcDateTime, (await server.FindRawAsync(server.Database.ApiTokens, created.PersonalAccessToken.Id)).LastUsedAt);

        server.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        await tokenClient.GetAsync<UserResponse>("/api/users/me");
        var lastUsedAt = ApiTestServer.DefaultStartTime.AddSeconds(90);
        Assert.Equal(lastUsedAt.UtcDateTime, (await server.FindRawAsync(server.Database.ApiTokens, created.PersonalAccessToken.Id)).LastUsedAt);
        Assert.Equal(lastUsedAt, Assert.Single(await alice.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath)).LastUsedAt);
    }

    [Fact]
    public async Task Token_CannotManageTokens()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var created = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Script"), HttpStatusCode.Created);
        var tokenClient = server.CreateClient(created.Token);

        await tokenClient.SendExpectingProblemAsync(HttpMethod.Get, TokensPath, null, HttpStatusCode.Unauthorized);
        await tokenClient.SendExpectingProblemAsync(HttpMethod.Post, TokensPath, new CreatePersonalAccessTokenRequest("Another"), HttpStatusCode.Unauthorized);
        await tokenClient.SendExpectingProblemAsync(HttpMethod.Delete, $"{TokensPath}/{created.PersonalAccessToken.Id}", null, HttpStatusCode.Unauthorized);
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath));
    }

    [Fact]
    public async Task Revoke_StopsTheTokenFromAuthenticating()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var revoked = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Old"), HttpStatusCode.Created);
        var kept = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Current"), HttpStatusCode.Created);
        await server.CreateClient(revoked.Token).GetAsync<UserResponse>("/api/users/me");

        await alice.Client.DeleteAsync($"{TokensPath}/{revoked.PersonalAccessToken.Id}");

        await server.CreateClient(revoked.Token).SendExpectingProblemAsync(HttpMethod.Get, "/api/users/me", null, HttpStatusCode.Unauthorized);
        await server.CreateClient(kept.Token).GetAsync<UserResponse>("/api/users/me");
        Assert.Equal([kept.PersonalAccessToken.Id], (await alice.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath)).Select(token => token.Id));
        Assert.NotNull((await server.FindRawAsync(server.Database.ApiTokens, revoked.PersonalAccessToken.Id)).RevokedAt);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TokensPath}/{revoked.PersonalAccessToken.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TokensPath}/{Guid.CreateVersion7()}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tokens_AreScopedToTheirOwner()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob");
        var alicesToken = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Alice"), HttpStatusCode.Created);
        var alicesTask = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Private"), HttpStatusCode.Created);
        var bobsToken = await bob.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Bob"), HttpStatusCode.Created);

        Assert.Equal([bobsToken.PersonalAccessToken.Id], (await bob.Client.GetAsync<IReadOnlyList<PersonalAccessTokenResponse>>(TokensPath)).Select(token => token.Id));
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TokensPath}/{alicesToken.PersonalAccessToken.Id}", null, HttpStatusCode.NotFound);
        await server.CreateClient(bobsToken.Token).SendExpectingProblemAsync(HttpMethod.Get, $"/api/tasks/{alicesTask.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal("bob", (await server.CreateClient(bobsToken.Token).GetAsync<UserResponse>("/api/users/me")).UserName);
        await server.CreateClient(alicesToken.Token).GetAsync<UserResponse>("/api/users/me");
    }

    [Fact]
    public async Task UnknownOrMalformedTokens_AreRejectedWhileSessionTokensKeepWorking()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var created = await alice.Client.PostAsync<CreatedPersonalAccessTokenResponse>(TokensPath, new CreatePersonalAccessTokenRequest("Script"), HttpStatusCode.Created);
        var tamperedToken = created.Token[..^1] + (created.Token[^1] == 'a' ? 'b' : 'a');

        using var unauthorized = await server.CreateClient(PersonalAccessTokenFormat.Prefix + new string('x', 40)).SendAsync(HttpMethod.Get, "/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("Bearer", unauthorized.Headers.WwwAuthenticate.ToString());
        await server.CreateClient(tamperedToken).SendExpectingProblemAsync(HttpMethod.Get, "/api/users/me", null, HttpStatusCode.Unauthorized);
        await server.CreateClient("edk_short").SendExpectingProblemAsync(HttpMethod.Get, "/api/users/me", null, HttpStatusCode.Unauthorized);
        await server.CreateClient("not-a-token").SendExpectingProblemAsync(HttpMethod.Get, "/api/users/me", null, HttpStatusCode.Unauthorized);
        Assert.Equal("alice", (await alice.Client.GetAsync<UserResponse>("/api/users/me")).UserName);
    }
}
