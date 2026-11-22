using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class IdempotencyTests(MongoDbContainerFixture mongoDb)
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task Post_RepeatedWithTheSameKey_ReplaysTheStoredResponseAndCreatesOneTask()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var key = Guid.CreateVersion7().ToString();
        var request = new CreateTaskRequest("Buy milk");

        using var first = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, request, key);
        using var second = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, request, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(first.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(IdempotencyMiddleware.ReplayedHeaderName)));
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(first.Content.Headers.ContentType?.MediaType, second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
    }

    [Fact]
    public async Task Post_WithoutAKey_CreatesATaskEachTime()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);

        Assert.Equal(2, (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath)).Count);
    }

    [Fact]
    public async Task PostStep_RepeatedWithTheSameKey_AddsOneStep()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Pack"), HttpStatusCode.Created);
        var key = Guid.CreateVersion7().ToString();

        using var first = await alice.Client.SendAsync(HttpMethod.Post, $"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Books"), key);
        using var second = await alice.Client.SendAsync(HttpMethod.Post, $"{TasksPath}/{task.Id}/steps", new CreateTaskStepRequest("Books"), key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single((await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}")).Steps);
    }

    [Fact]
    public async Task Patch_ReplayedAfterALaterChange_ReturnsTheStoredResponseWithoutApplyingItAgain()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var key = Guid.CreateVersion7().ToString();
        var request = new UpdateTaskRequest { Title = "Buy oat milk" };

        using var first = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", request, key);
        var firstResponse = ApiClient.DeserializeJson<TaskItemResponse>(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var later = await alice.Client.PatchAsync<TaskItemResponse>($"{TasksPath}/{task.Id}", new UpdateTaskRequest { Title = "Buy soy milk" });
        using var replayed = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", request, key);

        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.True(replayed.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        var replayedResponse = ApiClient.DeserializeJson<TaskItemResponse>(await replayed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Buy oat milk", replayedResponse.Title);
        Assert.Equal(firstResponse.ChangeVersion, replayedResponse.ChangeVersion);
        var stored = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.Equal("Buy soy milk", stored.Title);
        Assert.Equal(later.ChangeVersion, stored.ChangeVersion);
    }

    [Fact]
    public async Task Delete_RepeatedWithTheSameKey_Returns204AgainWhileADeleteWithoutAKeyReturns404()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var note = await alice.Client.PostAsync<NoteResponse>("/api/notes", new CreateNoteRequest("Shopping"), HttpStatusCode.Created);
        var key = Guid.CreateVersion7().ToString();
        var noteKey = Guid.CreateVersion7().ToString();

        using var first = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key);
        using var second = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key);
        using var firstNote = await alice.Client.SendAsync(HttpMethod.Delete, $"/api/notes/{note.Id}", idempotencyKey: noteKey);
        using var secondNote = await alice.Client.SendAsync(HttpMethod.Delete, $"/api/notes/{note.Id}", idempotencyKey: noteKey);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.True(second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        Assert.Equal(HttpStatusCode.NoContent, secondNote.StatusCode);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"/api/notes/{note.Id}", null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Requests_ReusingAKeyForADifferentRequest_AreRejectedWith422()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var key = Guid.CreateVersion7().ToString();
        using var first = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsImportant = true }, key);

        using var differentBody = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", new UpdateTaskRequest { IsImportant = false }, key);
        using var differentMethod = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, differentBody.StatusCode);
        Assert.Equal(ApiClient.ProblemMediaType, differentBody.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, differentMethod.StatusCode);
        var stored = await alice.Client.GetAsync<TaskItemResponse>($"{TasksPath}/{task.Id}");
        Assert.True(stored.IsImportant);
    }

    [Fact]
    public async Task Keys_AreScopedToTheUser()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        var key = Guid.CreateVersion7().ToString();

        using var alicesResponse = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Shared key"), key);
        using var bobsResponse = await bob.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Shared key"), key);

        Assert.Equal(HttpStatusCode.Created, bobsResponse.StatusCode);
        Assert.False(bobsResponse.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        Assert.Single(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
        Assert.Single(await bob.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
    }

    [Fact]
    public async Task ErrorResponses_AreStoredExceptConflicts()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var validationKey = Guid.CreateVersion7().ToString();
        var conflictKey = Guid.CreateVersion7().ToString();
        var staleRequest = new UpdateTaskRequest { Title = "Stale", ExpectedChangeVersion = task.ChangeVersion - 1 };

        using var invalid = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest(" "), validationKey);
        using var invalidAgain = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest(" "), validationKey);
        using var conflict = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", staleRequest, conflictKey);
        using var conflictAgain = await alice.Client.SendAsync(HttpMethod.Patch, $"{TasksPath}/{task.Id}", staleRequest, conflictKey);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAgain.StatusCode);
        Assert.Equal(ApiClient.ProblemMediaType, invalidAgain.Content.Headers.ContentType?.MediaType);
        Assert.True(invalidAgain.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(task.Id, ApiClient.DeserializeJson<TaskItemResponse>(await conflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Equal(HttpStatusCode.Conflict, conflictAgain.StatusCode);
        Assert.False(conflictAgain.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
        Assert.Null(await FindKeyAsync(server, alice, conflictKey));
    }

    [Fact]
    public async Task Request_WhileTheSameKeyIsProcessing_Returns409UntilTheClaimIsAbandoned()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var key = Guid.CreateVersion7().ToString();
        var request = new CreateTaskRequest("Buy milk");
        using (var first = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, request, key)) Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var identifier = IdempotencyKeyDocument.CreateId(alice.Id, key);
        var unfinished = Builders<IdempotencyKeyDocument>.Update.Set(document => document.CompletedAt, null).Set(document => document.ClaimedAt, server.TimeProvider.GetUtcNow().UtcDateTime);
        await server.Database.IdempotencyKeys.UpdateOneAsync(document => document.Id == identifier, unfinished, cancellationToken: TestContext.Current.CancellationToken);

        using var whileProcessing = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, request, key);
        Assert.Equal(HttpStatusCode.Conflict, whileProcessing.StatusCode);
        Assert.Equal(ApiClient.ProblemMediaType, whileProcessing.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(whileProcessing.Headers.RetryAfter);

        await server.AdvanceTimeAsync(IdempotencyKeyStore.ProcessingTimeout + TimeSpan.FromSeconds(1), alice);
        using var afterTimeout = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, request, key);
        Assert.Equal(HttpStatusCode.Created, afterTimeout.StatusCode);
        Assert.Equal(2, (await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath)).Count);
        Assert.NotNull((await FindKeyAsync(server, alice, key))?.CompletedAt);
    }

    [Fact]
    public async Task Keys_ExpireAfterTheRetentionPeriod()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var task = await alice.Client.PostAsync<TaskItemResponse>(TasksPath, new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        var key = Guid.CreateVersion7().ToString();
        using (var first = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key)) Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        await server.AdvanceTimeAsync(IdempotencyKeyStore.Retention - TimeSpan.FromMinutes(1), alice);
        using (var withinRetention = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key)) Assert.Equal(HttpStatusCode.NoContent, withinRetention.StatusCode);

        await server.AdvanceTimeAsync(TimeSpan.FromMinutes(2), alice);
        using var afterRetention = await alice.Client.SendAsync(HttpMethod.Delete, $"{TasksPath}/{task.Id}", idempotencyKey: key);
        Assert.Equal(HttpStatusCode.NotFound, afterRetention.StatusCode);
    }

    [Fact]
    public async Task InvalidKeys_AreRejectedAndAnonymousRequestsIgnoreTheHeader()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        using var tooLong = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Buy milk"), new string('k', IdempotencyMiddleware.MaximumKeyLength + 1));
        using var withSpace = await alice.Client.SendAsync(HttpMethod.Post, TasksPath, new CreateTaskRequest("Buy milk"), "two words");
        var key = Guid.CreateVersion7().ToString();
        using var firstLogin = await server.Anonymous.SendAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest("alice", ApiTestServer.DefaultPassword, "Phone"), key);
        using var secondLogin = await server.Anonymous.SendAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest("alice", ApiTestServer.DefaultPassword, "Phone"), key);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(ApiClient.ProblemMediaType, tooLong.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, withSpace.StatusCode);
        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<TaskItemResponse>>(TasksPath));
        var firstTokens = ApiClient.DeserializeJson<AuthenticationResponse>(await firstLogin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var secondTokens = ApiClient.DeserializeJson<AuthenticationResponse>(await secondLogin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(firstTokens.RefreshToken, secondTokens.RefreshToken);
        Assert.False(secondLogin.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName));
    }

    private static async Task<IdempotencyKeyDocument?> FindKeyAsync(ApiTestServer server, TestUser user, string key)
    {
        var identifier = IdempotencyKeyDocument.CreateId(user.Id, key);
        return await server.Database.IdempotencyKeys.Find(document => document.Id == identifier).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }
}
