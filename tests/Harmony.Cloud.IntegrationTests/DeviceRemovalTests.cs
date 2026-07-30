using System.Net;
using System.Text.Json;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

[Collection(CloudApiCollection.Name)]
public sealed class DeviceRemovalTests(CloudApiFixture fixture)
{
    [Fact]
    public async Task Removing_device_hides_it_and_ends_its_active_session()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        var started = await account.StartSessionAsync(
            source, target, TestAccount.State(["aaaaaaaaaaa", "bbbbbbbbbbb"]));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        var removed = await account.Client.DeleteAsync(TestAccount.Route($"devices/{target}"));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Null(await account.GetSessionAsync());

        var devicesResponse = await account.Client.GetAsync(
            TestAccount.Route($"playback/devices?currentDeviceId={source}"));
        devicesResponse.EnsureSuccessStatusCode();
        var devices = JsonDocument.Parse(await devicesResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(
            devices.RootElement.EnumerateArray(),
            device => device.GetProperty("deviceId").GetGuid() == target);

        var pending = await account.Client.GetAsync(
            TestAccount.Route($"playback/commands?deviceId={target}"));
        pending.EnsureSuccessStatusCode();
        var pendingCommands = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        Assert.Empty(pendingCommands.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Device_removal_is_scoped_to_the_signed_in_account()
    {
        var owner = TestAccount.New(fixture);
        var deviceId = await owner.RegisterDeviceAsync("Harmony Android", "android");
        var stranger = TestAccount.New(fixture);

        var removal = await stranger.Client.DeleteAsync(
            TestAccount.Route($"devices/{deviceId}"));

        Assert.Equal(HttpStatusCode.NotFound, removal.StatusCode);
        var devicesResponse = await owner.Client.GetAsync(
            TestAccount.Route($"playback/devices?currentDeviceId={Guid.NewGuid()}"));
        devicesResponse.EnsureSuccessStatusCode();
        var devices = JsonDocument.Parse(await devicesResponse.Content.ReadAsStringAsync());
        Assert.Contains(
            devices.RootElement.EnumerateArray(),
            device => device.GetProperty("deviceId").GetGuid() == deviceId);
    }
}
