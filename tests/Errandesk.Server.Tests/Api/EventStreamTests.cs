using System.Net;
using System.Net.ServerSentEvents;
using Errandesk.Contracts;
using Errandesk.Server.Services;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Errandesk.Server.Tests.Api;

public sealed class EventStreamTests(MongoDbContainerFixture mongoDb)
{
    private const string EventsPath = "/api/events";
    private static readonly TimeSpan s_readTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Events_EmitChangedWithTheNewVersionWhenTheUsersDataChanges()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(alice, "bob");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_readTimeout);

        using var response = await alice.Client.OpenStreamAsync(EventsPath, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApiClient.EventStreamMediaType, response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var events = SseParser.Create(stream).EnumerateAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        await bob.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Bob's task"), HttpStatusCode.Created);
        var task = await alice.Client.PostAsync<TaskItemResponse>("/api/tasks", new CreateTaskRequest("Buy milk"), HttpStatusCode.Created);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(ServerEventNames.Changed, events.Current.EventType);
        Assert.Equal(task.ChangeVersion, ApiClient.DeserializeJson<ChangedEventData>(events.Current.Data).Version);

        await alice.Client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.True(await events.MoveNextAsync());
        Assert.True(ApiClient.DeserializeJson<ChangedEventData>(events.Current.Data).Version > task.ChangeVersion);
    }

    [Fact]
    public async Task Events_SendKeepAliveCommentsWhileIdle()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, configureServices: services => services.AddSingleton(new EventStreamOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(100) }));
        var alice = await server.RegisterAsync("alice");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_readTimeout);

        using var response = await alice.Client.OpenStreamAsync(EventsPath, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        List<string> lines = [];
        while (lines.Count(line => line == ": keep-alive") < 2) lines.Add(await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException());

        Assert.Equal(": connected", lines[0]);
    }

    [Fact]
    public async Task Events_WithoutAToken_ReturnUnauthorized()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);

        await server.Anonymous.SendExpectingAsync(HttpMethod.Get, EventsPath, null, HttpStatusCode.Unauthorized);
    }
}
