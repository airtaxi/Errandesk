using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;

namespace Errandesk.Server.Tests.Api;

public sealed class UserEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string CurrentUserPath = "/api/users/me";
    private const string InvitesPath = "/api/admin/invites";

    [Fact]
    public async Task PatchCurrentUser_ChangesOnlySpecifiedFields()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice", timeZone: "Europe/Berlin");

        var renamed = await alice.Client.PatchAsync<UserResponse>(CurrentUserPath, new UpdateUserRequest { DisplayName = "Alice Kim" });
        var relocated = await alice.Client.PatchAsync<UserResponse>(CurrentUserPath, new UpdateUserRequest { TimeZone = "Asia/Seoul", Language = UserLanguages.Korean });

        Assert.Equal("Alice Kim", renamed.DisplayName);
        Assert.Equal("Europe/Berlin", renamed.TimeZone);
        Assert.Equal(UserLanguages.English, renamed.Language);
        Assert.Equal("Alice Kim", relocated.DisplayName);
        Assert.Equal("Asia/Seoul", relocated.TimeZone);
        Assert.Equal(UserLanguages.Korean, relocated.Language);
        Assert.True(relocated.ChangeVersion > renamed.ChangeVersion);
        Assert.Equal(relocated, await alice.Client.GetAsync<UserResponse>(CurrentUserPath));
    }

    [Fact]
    public async Task PatchCurrentUser_WithInvalidValues_ReturnsBadRequest()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, CurrentUserPath, new UpdateUserRequest { DisplayName = " " }, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, CurrentUserPath, new UpdateUserRequest { Language = "de" }, HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Patch, CurrentUserPath, new UpdateUserRequest { TimeZone = "Korea Standard Time" }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_ChecksTheCurrentPasswordAndSignsOutOtherSessions()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var otherSession = await server.LoginAsync("alice");
        const string NewPassword = "a much better password";

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{CurrentUserPath}/password", new ChangePasswordRequest("wrong password", NewPassword), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, $"{CurrentUserPath}/password", new ChangePasswordRequest(ApiTestServer.DefaultPassword, "short"), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingAsync(HttpMethod.Post, $"{CurrentUserPath}/password", new ChangePasswordRequest(ApiTestServer.DefaultPassword, NewPassword), HttpStatusCode.NoContent);

        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest("alice", ApiTestServer.DefaultPassword), HttpStatusCode.Unauthorized);
        await server.LoginAsync("alice", NewPassword);
        await server.Anonymous.PostAsync<AuthenticationResponse>("/api/auth/refresh", new RefreshTokenRequest(alice.RefreshToken));
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, "/api/auth/refresh", new RefreshTokenRequest(otherSession.RefreshToken), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdministratorEndpoints_AreForbiddenForOtherUsers()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var administrator = await server.RegisterAsync("alice");
        var bob = await server.RegisterInvitedAsync(administrator, "bob");

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, "/api/admin/users", null, HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Get, InvitesPath, null, HttpStatusCode.Forbidden);
        await bob.Client.SendExpectingProblemAsync(HttpMethod.Post, InvitesPath, new CreateInviteRequest(), HttpStatusCode.Forbidden);

        var users = await administrator.Client.GetAsync<IReadOnlyList<UserResponse>>("/api/admin/users");
        Assert.Equal(["alice", "bob"], users.Select(user => user.UserName));
    }

    [Fact]
    public async Task Invites_CanBeCreatedListedAndDeleted()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var administrator = await server.RegisterAsync("alice");

        var invite = await administrator.Client.PostAsync<InviteResponse>(InvitesPath, new CreateInviteRequest(), HttpStatusCode.Created);
        var longInvite = await administrator.Client.PostAsync<InviteResponse>(InvitesPath, new CreateInviteRequest(30), HttpStatusCode.Created);
        await administrator.Client.SendExpectingProblemAsync(HttpMethod.Post, InvitesPath, new CreateInviteRequest(0), HttpStatusCode.BadRequest);

        Assert.Equal(12, invite.Code.Length);
        Assert.NotEqual(invite.Code, longInvite.Code);
        Assert.Equal(administrator.Id, invite.CreatedByUserId);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddDays(7), invite.ExpiresAt);
        Assert.Equal(ApiTestServer.DefaultStartTime.AddDays(30), longInvite.ExpiresAt);
        Assert.Null(invite.UsedAt);
        Assert.Equal(2, (await administrator.Client.GetAsync<IReadOnlyList<InviteResponse>>(InvitesPath)).Count);

        await administrator.Client.DeleteAsync($"{InvitesPath}/{invite.Id}");

        await administrator.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{InvitesPath}/{invite.Id}", null, HttpStatusCode.NotFound);
        Assert.Equal([longInvite.Id], (await administrator.Client.GetAsync<IReadOnlyList<InviteResponse>>(InvitesPath)).Select(remaining => remaining.Id));
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Post, "/api/auth/register", new RegisterRequest("bob", ApiTestServer.DefaultPassword, "UTC", UserLanguages.English, InviteCode: invite.Code), HttpStatusCode.BadRequest);
    }
}
