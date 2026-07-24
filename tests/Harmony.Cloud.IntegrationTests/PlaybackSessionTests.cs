using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

[Collection(CloudApiCollection.Name)]
public sealed class PlaybackSessionTests(CloudApiFixture fixture)
{
    [Fact]
    public async Task Session_start_stores_v2_state_and_names_the_target()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();

        var started = await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa", "bbbbbbbbbbb"]));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        var session = await account.GetSessionAsync();
        Assert.NotNull(session);
        Assert.Equal(target, session.Value.GetProperty("targetDeviceId").GetGuid());
        var state = session.Value.GetProperty("state");
        Assert.Equal(2, state.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, state.GetProperty("queueIds").GetArrayLength());
        // Progress starts clean rather than inheriting anything.
        Assert.Equal(0, session.Value.GetProperty("positionMs").GetInt64());
        Assert.False(session.Value.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task Only_one_session_is_active_per_account()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));
        var first = (await account.GetSessionAsync())!.Value.GetProperty("sessionId").GetGuid();

        await account.StartSessionAsync(source, target, TestAccount.State(["bbbbbbbbbbb"]));

        var second = (await account.GetSessionAsync())!.Value.GetProperty("sessionId").GetGuid();
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(1, "unsupported_schema_version")]
    [InlineData(3, "unsupported_schema_version")]
    public async Task Session_start_rejects_a_foreign_schema_version(int version, string expectedCode)
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        var state = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = version,
            queueIds = new[] { "aaaaaaaaaaa" },
            index = 0
        });

        var response = await account.StartSessionAsync(source, target, state);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Session_start_rejects_an_index_outside_the_queue()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        var state = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            queueIds = new[] { "aaaaaaaaaaa" },
            index = 5
        });

        var response = await account.StartSessionAsync(source, target, state);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_index", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Session_state_rejects_a_payload_carrying_a_url_key()
    {
        // Guards the IsPortable deny-list: reintroducing artwork or stream urls must fail loudly
        // rather than quietly shipping playable links through the account channel.
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        var state = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            queueIds = new[] { "aaaaaaaaaaa" },
            index = 0,
            thumbnailUrl = "https://example/a.jpg"
        });

        var response = await account.StartSessionAsync(source, target, state);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_stale_queue_revision_does_not_roll_the_queue_backwards()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"], revision: 1));

        var newer = await account.PostAsync("playback/session/state", new
        {
            deviceId = target,
            state = TestAccount.State(["bbbbbbbbbbb", "ccccccccccc"], revision: 5)
        });
        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);

        var stale = await account.PostAsync("playback/session/state", new
        {
            deviceId = target,
            state = TestAccount.State(["ddddddddddd"], revision: 2)
        });

        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("\"applied\":false", await stale.Content.ReadAsStringAsync());
        var state = (await account.GetSessionAsync())!.Value.GetProperty("state");
        Assert.Equal(2, state.GetProperty("queueIds").GetArrayLength());
    }

    [Fact]
    public async Task Only_the_audio_target_may_publish_durable_state()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        var response = await account.PostAsync("playback/session/state", new
        {
            deviceId = source,
            state = TestAccount.State(["bbbbbbbbbbb"], revision: 9)
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Switching_the_target_moves_the_session_and_clears_stale_progress()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));
        var sessionId = (await account.GetSessionAsync())!.Value.GetProperty("sessionId").GetGuid();

        var response = await account.PostAsync("playback/session/target", new
        {
            sourceDeviceId = target,
            targetDeviceId = source,
            state = TestAccount.State(["aaaaaaaaaaa"], revision: 2)
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var session = (await account.GetSessionAsync())!.Value;
        // Same session, new owner — a retarget is not a new session.
        Assert.Equal(sessionId, session.GetProperty("sessionId").GetGuid());
        Assert.Equal(source, session.GetProperty("targetDeviceId").GetGuid());
        Assert.False(session.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task Ending_a_session_leaves_no_active_session()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        var deleted = await account.Client.DeleteAsync(TestAccount.Route("playback/session"));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Null(await account.GetSessionAsync());
    }

    [Fact]
    public async Task A_command_can_be_acknowledged_exactly_once_and_then_disappears()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        var submitted = await account.PostAsync("playback/session/command", new
        {
            sourceDeviceId = source,
            targetDeviceId = target,
            type = "pause",
            payload = new { }
        });
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var commandId = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync())
            .RootElement.GetProperty("commandId").GetGuid();

        var pending = await account.Client.GetFromJsonAsync<JsonElement>(
            TestAccount.Route($"playback/commands?deviceId={target}"));
        Assert.Contains(pending.EnumerateArray(), x => x.GetProperty("commandId").GetGuid() == commandId);

        var acked = await account.PostAsync($"playback/commands/{commandId}/ack",
            new { targetDeviceId = target, applied = true });
        Assert.Equal(HttpStatusCode.NoContent, acked.StatusCode);

        var afterAck = await account.Client.GetFromJsonAsync<JsonElement>(
            TestAccount.Route($"playback/commands?deviceId={target}"));
        Assert.DoesNotContain(afterAck.EnumerateArray(), x => x.GetProperty("commandId").GetGuid() == commandId);
    }

    [Fact]
    public async Task A_command_for_a_device_that_is_not_the_target_is_a_conflict()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        var response = await account.PostAsync("playback/session/command", new
        {
            sourceDeviceId = target,
            targetDeviceId = source,
            type = "pause",
            payload = new { }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Another_account_cannot_see_this_accounts_session()
    {
        var account = TestAccount.New(fixture);
        var (source, target) = await account.RegisterTwoDevicesAsync();
        await account.StartSessionAsync(source, target, TestAccount.State(["aaaaaaaaaaa"]));

        var stranger = TestAccount.New(fixture);

        Assert.Null(await stranger.GetSessionAsync());
    }
}
