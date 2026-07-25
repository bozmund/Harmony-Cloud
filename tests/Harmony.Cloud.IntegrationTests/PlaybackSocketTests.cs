using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

[Collection(CloudApiCollection.Name)]
public sealed class PlaybackSocketTests(CloudApiFixture fixture)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task An_upgrade_without_an_authenticated_subject_is_rejected()
    {
        var client = fixture.Factory.Server.CreateWebSocketClient();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync(SocketUri(Guid.NewGuid()), CancellationToken.None));

        Assert.Contains("401", failure.Message);
    }

    [Fact]
    public async Task A_plain_get_without_an_upgrade_is_a_bad_request()
    {
        var account = TestAccount.New(fixture);

        var response = await account.Client.GetAsync($"/cloud/v1/playback/socket?deviceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_device_cannot_open_a_socket()
    {
        var account = TestAccount.New(fixture);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            ConnectAsync(account, Guid.NewGuid()));

        Assert.Contains("404", failure.Message);
    }

    [Fact]
    public async Task Connecting_replays_the_active_session_snapshot()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa", "bbbbbbbbbbb"]));

        using var socket = await ConnectAsync(account, source);
        var frame = await ReceiveAsync(socket);

        Assert.Equal("sessionSnapshot", frame.GetProperty("type").GetString());
        Assert.Equal(target, frame.GetProperty("targetDeviceId").GetGuid());
        Assert.Equal(2, frame.GetProperty("state").GetProperty("queueIds").GetArrayLength());
    }

    [Fact]
    public async Task A_command_created_while_the_target_was_offline_is_replayed_on_connect()
    {
        // The regression that broke handoff: a push to a device with no socket reaches nobody, and
        // Windows has no FCM to be woken by. Without replay the command is lost for good.
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        // The target never connected while the session started, so it missed the handoff push.
        using var socket = await ConnectAsync(account, target);

        var snapshot = await ReceiveAsync(socket);
        Assert.Equal("sessionSnapshot", snapshot.GetProperty("type").GetString());
        var replayed = await ReceiveAsync(socket);
        Assert.Equal("command", replayed.GetProperty("type").GetString());
        Assert.Equal("handoff", replayed.GetProperty("commandType").GetString());
    }

    [Fact]
    public async Task An_acknowledged_command_is_not_replayed_again()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using (var first = await ConnectAsync(account, target))
        {
            await ReceiveAsync(first); // snapshot
            var command = await ReceiveAsync(first);
            await SendAsync(first, new
            {
                type = "ack",
                commandId = command.GetProperty("commandId").GetGuid(),
                applied = true,
            });
            await WaitForNoPendingCommandsAsync(account, target);
        }

        using var second = await ConnectAsync(account, target);
        var snapshot = await ReceiveAsync(second);
        Assert.Equal("sessionSnapshot", snapshot.GetProperty("type").GetString());
        await Assert.ThrowsAnyAsync<Exception>(() => ReceiveWithinAsync(second, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task The_snapshot_carries_persisted_progress_so_a_late_joiner_can_resume()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using (var audioTarget = await ConnectTargetAsync(account, target))
        {
            await SendAsync(audioTarget, new
            {
                type = "progress",
                currentSongId = "aaaaaaaaaaa",
                positionMs = 42_000,
                durationMs = 213_000,
                playing = true,
                speed = 1.0,
                publishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            await WaitForAsync(account, x => x.GetProperty("positionMs").GetInt64() == 42_000);
        }

        using var controller = await ConnectAsync(account, source);
        var snapshot = await ReceiveAsync(controller);

        Assert.Equal("aaaaaaaaaaa", snapshot.GetProperty("currentSongId").GetString());
        Assert.Equal(42_000, snapshot.GetProperty("positionMs").GetInt64());
        Assert.Equal(213_000, snapshot.GetProperty("durationMs").GetInt64());
        Assert.True(snapshot.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task A_command_arrives_on_the_targets_socket_with_its_payload_inline()
    {
        // The whole point of replacing the hub: no notify-then-fetch round trip.
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var socket = await ConnectTargetAsync(account, target);

        await account.PostAsync("playback/session/command", new
        {
            sourceDeviceId = source,
            targetDeviceId = target,
            type = "seek",
            payload = new { positionMs = 42_000 }
        });

        var frame = await ReceiveAsync(socket);
        Assert.Equal("command", frame.GetProperty("type").GetString());
        Assert.Equal("seek", frame.GetProperty("commandType").GetString());
        Assert.Equal(42_000, frame.GetProperty("payload").GetProperty("positionMs").GetInt32());
    }

    [Fact]
    public async Task Progress_from_the_target_reaches_the_controller_but_not_the_target()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var controller = await ConnectAsync(account, source);
        await ReceiveAsync(controller); // snapshot
        using var audioTarget = await ConnectTargetAsync(account, target);

        await SendAsync(audioTarget, new
        {
            type = "progress",
            currentSongId = "aaaaaaaaaaa",
            positionMs = 12_345,
            durationMs = 213_000,
            playing = true,
            speed = 1.0,
            publishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var frame = await ReceiveAsync(controller);
        Assert.Equal("progress", frame.GetProperty("type").GetString());
        Assert.Equal(12_345, frame.GetProperty("positionMs").GetInt64());
        Assert.Equal(213_000, frame.GetProperty("durationMs").GetInt64());
        Assert.True(frame.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task Progress_is_persisted_so_a_late_joiner_does_not_open_at_zero()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var audioTarget = await ConnectTargetAsync(account, target);
        await SendAsync(audioTarget, new
        {
            type = "progress",
            currentSongId = "aaaaaaaaaaa",
            positionMs = 55_000,
            durationMs = 213_000,
            playing = true,
            speed = 1.0,
            publishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        // The first progress frame always persists: it is a song/playing-state change.
        var session = await WaitForAsync(account, x => x.GetProperty("positionMs").GetInt64() == 55_000);
        Assert.Equal("aaaaaaaaaaa", session.GetProperty("currentSongId").GetString());
        Assert.True(session.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task A_controller_cannot_publish_progress()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var controller = await ConnectAsync(account, source);
        await ReceiveAsync(controller); // snapshot
        await SendAsync(controller, new
        {
            type = "progress",
            currentSongId = "aaaaaaaaaaa",
            positionMs = 99_000,
            playing = true,
            speed = 1.0,
            publishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        // Give the server a chance to (wrongly) apply it before asserting nothing changed.
        await Task.Delay(500);
        var session = await account.GetSessionAsync();
        Assert.Equal(0, session!.Value.GetProperty("positionMs").GetInt64());
    }

    [Fact]
    public async Task Ending_a_session_notifies_every_device()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var controller = await ConnectAsync(account, source);
        await ReceiveAsync(controller); // snapshot

        await account.Client.DeleteAsync(TestAccount.Route("playback/session"));

        var frame = await ReceiveAsync(controller);
        Assert.Equal("sessionEnded", frame.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_malformed_frame_does_not_drop_the_connection()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        using var socket = await ConnectTargetAsync(account, target);

        await socket.SendAsync(Encoding.UTF8.GetBytes("{not json"), WebSocketMessageType.Text, true, CancellationToken.None);
        await SendAsync(socket, new { type = "ping" });

        // Still usable: a command sent afterwards must still arrive.
        await account.PostAsync("playback/session/command", new
        {
            sourceDeviceId = source,
            targetDeviceId = target,
            type = "pause",
            payload = new { }
        });
        var frame = await ReceiveAsync(socket);
        Assert.Equal("command", frame.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Replacing_a_devices_socket_keeps_its_persisted_presence_online()
    {
        var account = TestAccount.New(fixture);
        var deviceId = await account.RegisterDeviceAsync("Harmony Windows", "windows");

        using var first = await ConnectAsync(account, deviceId);
        using var replacement = await ConnectAsync(account, deviceId);

        // Registering the replacement aborts the old socket. Once its request finishes, it must not
        // overwrite the replacement's online presence in Postgres.
        await WaitForSocketClosureAsync(first);
        await Task.Delay(100);

        Assert.True(await fixture.IsRealtimeConnectedAsync(deviceId));
    }

    private static Uri SocketUri(Guid deviceId) =>
        new($"http://localhost/cloud/v1/playback/socket?deviceId={deviceId}");

    private async Task<WebSocket> ConnectAsync(TestAccount account, Guid deviceId)
    {
        var client = fixture.Factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            foreach (var header in account.Client.DefaultRequestHeaders)
                request.Headers[header.Key] = header.Value.ToArray();
        };
        return await client.ConnectAsync(SocketUri(deviceId), CancellationToken.None);
    }

    private static Task SendAsync(WebSocket socket, object frame) => socket.SendAsync(
        JsonSerializer.SerializeToUtf8Bytes(frame, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    private static Task<JsonElement> ReceiveAsync(WebSocket socket) =>
        ReceiveWithinAsync(socket, ReceiveTimeout);

    private static async Task<JsonElement> ReceiveWithinAsync(WebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        var received = await socket.ReceiveAsync(buffer, cancellation.Token);
        return JsonDocument.Parse(buffer.AsMemory(0, received.Count)).RootElement.Clone();
    }

    /// Connects as the audio target and consumes the snapshot plus the handoff the server replays
    /// for a device that was not connected when the session started.
    private async Task<WebSocket> ConnectTargetAsync(TestAccount account, Guid deviceId)
    {
        var socket = await ConnectAsync(account, deviceId);
        await ReceiveAsync(socket);
        await ReceiveAsync(socket);
        return socket;
    }

    private static async Task WaitForSocketClosureAsync(WebSocket socket)
    {
        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(ReceiveTimeout);
        await Assert.ThrowsAnyAsync<Exception>(() => socket.ReceiveAsync(buffer, timeout.Token));
    }

    /// The ack travels over the socket and commits asynchronously, so wait for it to land before
    /// asserting on what a later connection is replayed.
    private static async Task WaitForNoPendingCommandsAsync(TestAccount account, Guid deviceId)
    {
        var deadline = DateTime.UtcNow + ReceiveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var pending = await account.Client.GetFromJsonAsync<JsonElement>(
                TestAccount.Route($"playback/commands?deviceId={deviceId}"));
            if (pending.GetArrayLength() == 0) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("The command was never acknowledged.");
    }

    /// Progress persistence is asynchronous relative to the socket send, so poll briefly.
    private static async Task<JsonElement> WaitForAsync(TestAccount account, Func<JsonElement, bool> predicate)
    {
        var deadline = DateTime.UtcNow + ReceiveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var session = await account.GetSessionAsync();
            if (session is { } value && predicate(value)) return value;
            await Task.Delay(100);
        }
        throw new TimeoutException("The session never reached the expected state.");
    }
}
