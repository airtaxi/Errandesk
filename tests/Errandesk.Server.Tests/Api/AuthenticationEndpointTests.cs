using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class AuthenticationEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string RegisterPath = "/api/auth/register";
    private const string LoginPath = "/api/auth/login";
    private const string RefreshPath = "/api/auth/refresh";
    private const string LogoutPath = "/api/auth/logout";
    private const string CurrentUserPath = "/api/users/me";

    [Theory]
    [InlineData("invite")]
    [InlineData("disabled")]
    [InlineData("open")]
    public async Task Register_FirstUser_BecomesAdministratorWithDefaultList(string registrationMode)
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, registrationMode);

        var administrator = await server.RegisterAsync("alice");

        Assert.True(administrator.Authentication.User.IsAdministrator);
        Assert.Equal("alice", administrator.Authentication.User.DisplayName);
        Assert.False(string.IsNullOrEmpty(administrator.Authentication.AccessToken));
        Assert.False(string.IsNullOrEmpty(administrator.RefreshToken));
        var defaultList = await server.GetDefaultListAsync(administrator);
        Assert.Equal("Tasks", defaultList.Name);
        Assert.Equal(administrator.Id, (await administrator.Client.GetAsync<UserResponse>(CurrentUserPath)).Id);
    }

    [Fact]
    public async Task Register_SecondUserInDisabledMode_IsForbidden()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "disabled");
        await server.RegisterAsync("alice");

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("bob"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_SecondUserInOpenMode_NeedsNoInviteAndIsNotAdministrator()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        await server.RegisterAsync("alice");

        var bob = await server.RegisterAsync("bob");

        Assert.False(bob.Authentication.User.IsAdministrator);
    }

    [Fact]
    public async Task Register_InInviteMode_RequiresAValidSingleUseInvite()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "invite");
        var administrator = await server.RegisterAsync("alice");

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("bob"), HttpStatusCode.BadRequest);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("bob", "WRONGCODE123"), HttpStatusCode.BadRequest);

        var invite = await administrator.Client.PostAsync<InviteResponse>("/api/admin/invites", new CreateInviteRequest(), HttpStatusCode.Created);
        var bob = await server.RegisterAsync("bob", invite.Code.ToLowerInvariant());

        Assert.False(bob.Authentication.User.IsAdministrator);
        var invites = await administrator.Client.GetAsync<IReadOnlyList<InviteResponse>>("/api/admin/invites");
        var usedInvite = Assert.Single(invites);
        Assert.Equal(bob.Id, usedInvite.UsedByUserId);
        Assert.NotNull(usedInvite.UsedAt);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("carol", invite.Code), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithExpiredInvite_IsRejected()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "invite");
        var administrator = await server.RegisterAsync("alice");
        var invite = await administrator.Client.PostAsync<InviteResponse>("/api/admin/invites", new CreateInviteRequest(1), HttpStatusCode.Created);

        server.TimeProvider.Advance(TimeSpan.FromDays(2));

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("bob", invite.Code), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithTakenUserNameInAnotherCase_ReturnsConflict()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        await server.RegisterAsync("Alice");

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, CreateRegisterRequest("aLICE"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithInvalidValues_ReturnsBadRequest()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        RegisterRequest[] invalidRequests =
        [
            new("al", ApiTestServer.DefaultPassword, "UTC", "en"),
            new("alice", "short", "UTC", "en"),
            new("alice", ApiTestServer.DefaultPassword, "Mars/Olympus_Mons", "en"),
            new("alice", ApiTestServer.DefaultPassword, "UTC", "fr"),
        ];

        foreach (var invalidRequest in invalidRequests) await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RegisterPath, invalidRequest, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ChecksThePassword()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var session = await server.LoginAsync("ALICE");

        Assert.Equal(alice.Id, session.Authentication.User.Id);
        Assert.NotEqual(alice.RefreshToken, session.RefreshToken);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, LoginPath, new LoginRequest("alice", "wrong password"), HttpStatusCode.Unauthorized);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, LoginPath, new LoginRequest("nobody", ApiTestServer.DefaultPassword), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        await server.RegisterAsync("alice");

        for (var attempt = 1; attempt < AuthenticationRateLimiting.PermitLimit; attempt++) await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, LoginPath, new LoginRequest("alice", "wrong password"), HttpStatusCode.Unauthorized);

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, LoginPath, new LoginRequest("alice", ApiTestServer.DefaultPassword), HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refresh_RotatesTheTokenAndRevokesTheSessionOnReuse()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        var refreshed = await server.Anonymous.PostAsync<AuthenticationResponse>(RefreshPath, new RefreshTokenRequest(alice.RefreshToken));

        Assert.NotEqual(alice.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(alice.Id, refreshed.User.Id);
        await server.CreateClient(refreshed.AccessToken).GetAsync<UserResponse>(CurrentUserPath);

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.Unauthorized);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest(refreshed.RefreshToken), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReuseRevokesOnlyThatSession()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var otherSession = await server.LoginAsync("alice");

        await server.Anonymous.PostAsync<AuthenticationResponse>(RefreshPath, new RefreshTokenRequest(alice.RefreshToken));
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.Unauthorized);

        await server.Anonymous.PostAsync<AuthenticationResponse>(RefreshPath, new RefreshTokenRequest(otherSession.RefreshToken));
    }

    [Fact]
    public async Task Refresh_AfterTheRefreshTokenExpired_IsRejected()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        server.TimeProvider.Advance(TimeSpan.FromDays(31));

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.Unauthorized);
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest("not-a-token"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AccessToken_AfterItExpired_IsRejectedAndRefreshIssuesAWorkingOne()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        Assert.Equal(ApiTestServer.DefaultStartTime.AddMinutes(15), alice.Authentication.AccessTokenExpiresAt);

        server.TimeProvider.Advance(TimeSpan.FromMinutes(16));

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Get, CurrentUserPath, null, HttpStatusCode.Unauthorized);
        var refreshed = await server.Anonymous.PostAsync<AuthenticationResponse>(RefreshPath, new RefreshTokenRequest(alice.RefreshToken));
        await server.CreateClient(refreshed.AccessToken).GetAsync<UserResponse>(CurrentUserPath);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutAValidToken_ReturnUnauthorizedProblems()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Get, "/api/lists", null, HttpStatusCode.Unauthorized);
        await server.CreateClient("invalid.token.value").SendExpectingProblemAsync(HttpMethod.Get, "/api/tasks", null, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesTheSessionOfTheToken()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var otherSession = await server.LoginAsync("alice");

        await server.Anonymous.SendExpectingAsync(HttpMethod.Post, LogoutPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.NoContent);

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, RefreshPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.Unauthorized);
        await server.Anonymous.SendExpectingAsync(HttpMethod.Post, LogoutPath, new RefreshTokenRequest(alice.RefreshToken), HttpStatusCode.NoContent);
        await server.Anonymous.PostAsync<AuthenticationResponse>(RefreshPath, new RefreshTokenRequest(otherSession.RefreshToken));
    }

    private static RegisterRequest CreateRegisterRequest(string userName, string? inviteCode = null) => new(userName, ApiTestServer.DefaultPassword, "UTC", UserLanguages.English, InviteCode: inviteCode);
}
