using System.Net.Http.Json;
using System.Text.Json;

namespace Harmony.Cloud.IntegrationTests;

/// <summary>
/// A distinct signed-in account. Every instance gets a fresh subject so tests sharing the one
/// Postgres container never see each other's devices or sessions.
/// </summary>
public sealed class TestAccount
{
    private const string Prefix = "/cloud/v1/";

    public required HttpClient Client { get; init; }

    public static string Route(string path) => Prefix + path;

    public static TestAccount New(CloudApiFixture fixture)
    {
        var client = fixture.Factory.CreateClient();
        var accountId = $"test|{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Subject", accountId);
        return new TestAccount { Client = client };
    }

    /// A minimal valid v2 durable state document.
    public static JsonElement State(string[] queueIds, int index = 0, long revision = 1) =>
        JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            queueIds,
            index,
            currentSongId = queueIds[index],
            shuffle = false,
            repeat = false,
            queueLoop = false,
            queueRevision = revision
        });

    public async Task<(Guid Source, Guid Target)> RegisterTwoDevicesAsync()
    {
        var source = await RegisterDeviceAsync("Harmony Windows", "windows");
        var target = await RegisterDeviceAsync("Harmony Android", "android");
        return (source, target);
    }

    public async Task<Guid> RegisterDeviceAsync(string name, string platform)
    {
        var deviceId = Guid.NewGuid();
        var response = await PostAsync("devices/register", new
        {
            deviceId,
            name,
            platform,
            appVersion = "6.0.3"
        });
        response.EnsureSuccessStatusCode();
        return deviceId;
    }

    public Task<HttpResponseMessage> StartSessionAsync(Guid source, Guid target, JsonElement state) =>
        PostAsync("playback/session/start", new
        {
            sourceDeviceId = source,
            targetDeviceId = target,
            state
        });

    /// A device declaring itself the audio target for what it is already playing.
    public Task<HttpResponseMessage> ClaimSessionAsync(Guid device, JsonElement state) =>
        PostAsync("playback/session/claim", new
        {
            deviceId = device,
            state
        });

    public Task<HttpResponseMessage> PostAsync(string path, object body) =>
        Client.PostAsJsonAsync(Route(path), body);

    /// The active session, or null when the API answers 204.
    public async Task<JsonElement?> GetSessionAsync()
    {
        var response = await Client.GetAsync(Route("playback/session"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body).RootElement.Clone();
    }
}
