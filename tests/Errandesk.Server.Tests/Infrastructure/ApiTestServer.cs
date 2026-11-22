using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Notifications;
using Errandesk.Server.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// An in-memory server on its own database with a controllable clock, plus helpers to register users and manage invites.
/// </summary>
public sealed class ApiTestServer(TestApplication application, FakeTimeProvider timeProvider) : IAsyncDisposable
{
    public const string DefaultPassword = "correct horse battery";

    /// <summary>A fixed start time far enough ahead that MongoDB never expires stored refresh tokens during a test.</summary>
    public static readonly DateTimeOffset DefaultStartTime = new(2031, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan s_indexTimeout = TimeSpan.FromSeconds(30);

    public FakeTimeProvider TimeProvider => timeProvider;

    public ApiClient Anonymous { get; } = new(application.Client);

    public ErrandeskDatabase Database => application.Services.GetRequiredService<ErrandeskDatabase>();

    public IServiceProvider Services => application.Services;

    /// <param name="mongoDb">The shared MongoDB container.</param>
    /// <param name="registrationMode">Value of the registration mode variable.</param>
    /// <param name="startTime">Initial time of the fake clock.</param>
    /// <param name="chatClient">When set, the assistant is configured and talks to this chat client instead of a real endpoint.</param>
    /// <param name="configureServices">Replaces or adds services after the server registered its own.</param>
    /// <param name="additionalVariables">Further environment variables for the server settings.</param>
    public static async Task<ApiTestServer> StartAsync(MongoDbContainerFixture mongoDb, string registrationMode = "invite", DateTimeOffset? startTime = null, IChatClient? chatClient = null, Action<IServiceCollection>? configureServices = null, IReadOnlyDictionary<string, string>? additionalVariables = null)
    {
        var timeProvider = new FakeTimeProvider(startTime ?? DefaultStartTime);
        var variables = new Dictionary<string, string> { [ServerEnvironmentVariables.RegistrationMode] = registrationMode };
        foreach (var variable in additionalVariables ?? new Dictionary<string, string>()) variables[variable.Key] = variable.Value;
        if (chatClient is not null)
        {
            variables[ServerEnvironmentVariables.AiEndpoint] = "http://localhost:1/v1";
            variables[ServerEnvironmentVariables.AiModel] = "test-model";
        }

        var settings = TestServerSettings.Create(mongoDb.ConnectionString, MongoDbContainerFixture.CreateDatabaseName(), variables);
        var application = await TestApplication.StartAsync(settings, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.AddSingleton(new TombstonePurgeOptions { Interval = Timeout.InfiniteTimeSpan });
            services.AddSingleton(new MorningBriefingOptions { PollInterval = Timeout.InfiniteTimeSpan });
            if (chatClient is not null) services.AddSingleton(chatClient);
            configureServices?.Invoke(services);
        });
        await application.Services.GetRequiredService<MongoIndexInitializer>().Completion.WaitAsync(s_indexTimeout, TestContext.Current.CancellationToken);
        return new ApiTestServer(application, timeProvider);
    }

    public ApiClient CreateClient(string? accessToken = null) => new(application.Client, accessToken);

    /// <summary>
    /// Creates an HTTP client that sends the current access token of <paramref name="user"/>, with the server address as its base address.
    /// </summary>
    public HttpClient CreateHttpClient(TestUser user) => application.CreateClient(new BearerTokenHandler(() => user.Authentication.AccessToken));

    /// <summary>Creates an HTTP client that sends <paramref name="bearerToken"/>, with the server address as its base address.</summary>
    public HttpClient CreateHttpClient(string bearerToken) => application.CreateClient(new BearerTokenHandler(() => bearerToken));

    public async Task<TestUser> RegisterAsync(string userName, string? inviteCode = null, string timeZone = "UTC", string language = UserLanguages.English)
    {
        var authentication = await Anonymous.PostAsync<AuthenticationResponse>("/api/auth/register", new RegisterRequest(userName, DefaultPassword, timeZone, language, InviteCode: inviteCode, DeviceName: "Test device"));
        return new TestUser(authentication, CreateClient(authentication.AccessToken));
    }

    public async Task<TestUser> LoginAsync(string userName, string password = DefaultPassword)
    {
        var authentication = await Anonymous.PostAsync<AuthenticationResponse>("/api/auth/login", new LoginRequest(userName, password, "Second device"));
        return new TestUser(authentication, CreateClient(authentication.AccessToken));
    }

    /// <summary>Registers a user with a fresh invite created by <paramref name="administrator"/>.</summary>
    public async Task<TestUser> RegisterInvitedAsync(TestUser administrator, string userName, string timeZone = "UTC")
    {
        var invite = await administrator.Client.PostAsync<InviteResponse>("/api/admin/invites", new CreateInviteRequest(), HttpStatusCode.Created);
        return await RegisterAsync(userName, invite.Code, timeZone);
    }

    /// <summary>Moves the clock forward and refreshes the tokens of the given users so their access tokens stay valid.</summary>
    public async Task AdvanceTimeAsync(TimeSpan duration, params TestUser[] users)
    {
        timeProvider.Advance(duration);
        foreach (var user in users) user.Update(await Anonymous.PostAsync<AuthenticationResponse>("/api/auth/refresh", new RefreshTokenRequest(user.RefreshToken)));
    }

    public async Task<TaskListResponse> GetDefaultListAsync(TestUser user)
    {
        var lists = await user.Client.GetAsync<IReadOnlyList<TaskListResponse>>("/api/lists");
        return Assert.Single(lists, list => list.IsDefault);
    }

    public Task<TDocument> FindRawAsync<TDocument>(IMongoCollection<TDocument> collection, Guid identifier) where TDocument : DocumentBase => collection.Find(Builders<TDocument>.Filter.Eq(document => document.Id, identifier)).SingleAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => application.DisposeAsync();
}
