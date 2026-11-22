using System.Net;
using Errandesk.Contracts;
using Errandesk.Server.Tests.Infrastructure;
using MongoDB.Driver;

namespace Errandesk.Server.Tests.Api;

public sealed class DeviceEndpointTests(MongoDbContainerFixture mongoDb)
{
    private const string DevicesPath = "/api/devices";

    [Fact]
    public async Task Register_CreatesThenUpdatesTheSameDevice()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");
        var deviceId = Guid.CreateVersion7();

        var created = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(deviceId, "Android", "Pixel", "token-1", "0.1.0"), HttpStatusCode.Created);
        await server.AdvanceTimeAsync(TimeSpan.FromHours(1), alice);
        var updated = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(deviceId, DevicePlatforms.Android, "Pixel 9", "token-2", "0.2.0"));

        Assert.Equal(new DeviceResponse(deviceId, DevicePlatforms.Android, "Pixel", "0.1.0", true, ApiTestServer.DefaultStartTime, ApiTestServer.DefaultStartTime), created);
        Assert.Equal(new DeviceResponse(deviceId, DevicePlatforms.Android, "Pixel 9", "0.2.0", true, ApiTestServer.DefaultStartTime.AddHours(1), ApiTestServer.DefaultStartTime), updated);
        Assert.Equal([updated], await alice.Client.GetAsync<IReadOnlyList<DeviceResponse>>(DevicesPath));
        var stored = await server.FindRawAsync(server.Database.Devices, deviceId);
        Assert.Equal("token-2", stored.PushToken);
        Assert.Equal(alice.Id, stored.OwnerUserId);
    }

    [Fact]
    public async Task Register_ValidatesPlatformAndToken()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb);
        var alice = await server.RegisterAsync("alice");

        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), "blackberry"), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Windows, PushToken: "token"), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Ios, PushToken: "not-hex"), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, DevicesPath, new RegisterDeviceRequest(Guid.Empty, DevicePlatforms.Desktop), HttpStatusCode.BadRequest);
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Post, DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Android, PushToken: new string('a', 4097)), HttpStatusCode.BadRequest);

        var windows = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Windows, "  ", "  "), HttpStatusCode.Created);
        var iphone = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Ios, "iPhone", "ABCDEF0123"), HttpStatusCode.Created);

        Assert.False(windows.HasPushToken);
        Assert.Equal("Unknown device", windows.DeviceName);
        Assert.Equal("abcdef0123", (await server.FindRawAsync(server.Database.Devices, iphone.Id)).PushToken);
    }

    [Fact]
    public async Task Register_MovesATokenAndAnInstallationToTheUserWhoRegisteredLast()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob");
        var alicesPhone = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Android, PushToken: "shared-token"), HttpStatusCode.Created);
        var alicesTablet = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Android, PushToken: "tablet-token"), HttpStatusCode.Created);

        await bob.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Android, PushToken: "shared-token"), HttpStatusCode.Created);
        await bob.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(alicesTablet.Id, DevicePlatforms.Android, PushToken: "tablet-token"));

        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<DeviceResponse>>(DevicesPath));
        Assert.Equal(2, (await bob.Client.GetAsync<IReadOnlyList<DeviceResponse>>(DevicesPath)).Count);
        Assert.Null(await server.Database.Devices.Find(device => device.Id == alicesPhone.Id).FirstOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheUsersOwnDevice()
    {
        await using var server = await ApiTestServer.StartAsync(mongoDb, "open");
        var alice = await server.RegisterAsync("alice");
        var bob = await server.RegisterAsync("bob");
        var device = await alice.Client.PostAsync<DeviceResponse>(DevicesPath, new RegisterDeviceRequest(Guid.CreateVersion7(), DevicePlatforms.Ios, PushToken: "abcd"), HttpStatusCode.Created);

        await bob.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{DevicesPath}/{device.Id}", null, HttpStatusCode.NotFound);
        await alice.Client.DeleteAsync($"{DevicesPath}/{device.Id}");
        await alice.Client.SendExpectingProblemAsync(HttpMethod.Delete, $"{DevicesPath}/{device.Id}", null, HttpStatusCode.NotFound);

        Assert.Empty(await alice.Client.GetAsync<IReadOnlyList<DeviceResponse>>(DevicesPath));
        await server.Anonymous.SendExpectingProblemAsync(HttpMethod.Get, DevicesPath, null, HttpStatusCode.Unauthorized);
    }
}
