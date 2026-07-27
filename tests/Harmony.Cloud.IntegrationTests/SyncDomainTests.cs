using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Harmony.Cloud.Api.Domain;
using Harmony.Cloud.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

/// <summary>
/// Sync storage is split per domain: settings never share a table with songs, and song metadata
/// lives once per account in <c>cloud_songs</c> rather than being copied into every list that
/// references it.
/// </summary>
[Collection(CloudApiCollection.Name)]
public sealed class SyncDomainTests(CloudApiFixture fixture)
{
    private const string VideoId = "abcdefghijk";

    [Fact]
    public async Task Settings_and_songs_land_in_their_own_tables()
    {
        var account = TestAccount.New(fixture);
        var device = await account.RegisterDeviceAsync("Harmony Windows", "windows");

        var response = await SyncAsync(account, device, 0,
            Event(device, 1, "settings", EntityId("AppPrefs", "themeModeType"), Json(new { })),
            Event(device, 2, "favourites", EntityId("LIBFAV", VideoId), Song("Kamikaza")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = await OpenDbAsync();
        var accountId = await AccountIdAsync(db, device);

        Assert.Equal(1, await db.Settings.CountAsync(x => x.AccountId == accountId));
        Assert.Equal("themeModeType", (await db.Settings.SingleAsync(x => x.AccountId == accountId)).Key);
        Assert.Equal(1, await db.Favourites.CountAsync(x => x.AccountId == accountId));

        var song = await db.Songs.SingleAsync(x => x.AccountId == accountId);
        Assert.Equal(VideoId, song.VideoId);
        Assert.Equal("Kamikaza", song.Title);
    }

    [Fact]
    public async Task A_song_in_two_lists_produces_one_shared_song_row()
    {
        var account = TestAccount.New(fixture);
        var device = await account.RegisterDeviceAsync("Harmony Windows", "windows");

        await SyncAsync(account, device, 0,
            Event(device, 1, "favourites", EntityId("LIBFAV", VideoId), Song("Kamikaza")),
            Event(device, 2, "playlistSongs", EntityId("PL_local_1", "0"), Song("Kamikaza")),
            Event(device, 3, "recentlyPlayed", EntityId("LIBRP", "17692"), Song("Kamikaza")));

        await using var db = await OpenDbAsync();
        var accountId = await AccountIdAsync(db, device);

        Assert.Equal(1, await db.Songs.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Favourites.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.PlaylistSongs.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.RecentlyPlayed.CountAsync(x => x.AccountId == accountId));

        var membership = await db.PlaylistSongs.SingleAsync(x => x.AccountId == accountId);
        Assert.Equal("PL_local_1", membership.PlaylistId);
        Assert.Equal(VideoId, membership.VideoId);
    }

    [Fact]
    public async Task Unknown_domains_are_accepted_and_dropped_rather_than_rejected()
    {
        // Devices on an older build keep sending downloads and lyrics until they are rebuilt.
        // Rejecting the batch would wedge that device's sync permanently.
        var account = TestAccount.New(fixture);
        var device = await account.RegisterDeviceAsync("Harmony Android", "android");

        var response = await SyncAsync(account, device, 0,
            Event(device, 1, "hive-entry", EntityId("SongDownloads", VideoId), Song("Kamikaza")),
            Event(device, 2, "lyrics", EntityId("lyrics", VideoId), Json(new { text = "la la la" })),
            Event(device, 3, "favourites", EntityId("LIBFAV", VideoId), Song("Kamikaza")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        // Every event is acknowledged so the device's outbox drains...
        Assert.Equal(3, body.GetProperty("acceptedEventIds").GetArrayLength());

        await using var db = await OpenDbAsync();
        var accountId = await AccountIdAsync(db, device);
        // ...but only the known domain was stored.
        Assert.Equal(1, await db.Favourites.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Songs.CountAsync(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Changes_come_back_in_strict_revision_order_across_domain_tables()
    {
        var account = TestAccount.New(fixture);
        var writer = await account.RegisterDeviceAsync("Harmony Windows", "windows");
        var reader = await account.RegisterDeviceAsync("Harmony Android", "android");

        await SyncAsync(account, writer, 0,
            Event(writer, 1, "settings", EntityId("AppPrefs", "volume"), Json(new { })),
            Event(writer, 2, "favourites", EntityId("LIBFAV", VideoId), Song("Kamikaza")),
            Event(writer, 3, "playlists", EntityId("LibraryPlaylists", "PL_local_1"), Json(new { title = "Road trip" })),
            Event(writer, 4, "searchHistory", EntityId("searchQuery", "0"), JsonScalar("nucci")));

        var response = await SyncAsync(account, reader, 0);
        var body = await ReadAsync(response);
        var changes = body.GetProperty("changes");

        Assert.Equal(4, changes.GetArrayLength());
        var revisions = changes.EnumerateArray().Select(x => x.GetProperty("revision").GetInt64()).ToList();
        Assert.Equal(revisions.OrderBy(x => x), revisions);
        // The shared sequence keeps revisions globally unique even though the rows live in
        // four different tables.
        Assert.Equal(revisions.Distinct().Count(), revisions.Count);

        var types = changes.EnumerateArray().Select(x => x.GetProperty("entityType").GetString()).ToList();
        Assert.Equal(["settings", "favourites", "playlists", "searchHistory"], types);
        Assert.Equal(revisions[^1], body.GetProperty("checkpoint").GetInt64());
    }

    [Fact]
    public async Task Deleting_a_favourite_leaves_the_shared_song_row_intact()
    {
        // Other lists may still reference the song, so a membership delete must not remove it.
        var account = TestAccount.New(fixture);
        var device = await account.RegisterDeviceAsync("Harmony Windows", "windows");

        await SyncAsync(account, device, 0,
            Event(device, 1, "favourites", EntityId("LIBFAV", VideoId), Song("Kamikaza")),
            Event(device, 2, "playlistSongs", EntityId("PL_local_1", "0"), Song("Kamikaza")));
        await SyncAsync(account, device, 0,
            Event(device, 3, "favourites", EntityId("LIBFAV", VideoId), Json(new { }), operation: "delete"));

        await using var db = await OpenDbAsync();
        var accountId = await AccountIdAsync(db, device);

        var favourite = await db.Favourites.SingleAsync(x => x.AccountId == accountId);
        Assert.True(favourite.Tombstone);
        Assert.Equal(1, await db.Songs.CountAsync(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Every_domain_has_its_own_event_table()
    {
        var account = TestAccount.New(fixture);
        var device = await account.RegisterDeviceAsync("Harmony Windows", "windows");

        await SyncAsync(account, device, 0,
            Event(device, 1, "settings", EntityId("AppPrefs", "volume"), Json(new { })),
            Event(device, 2, "albums", EntityId("LibraryAlbums", "MPREb_1"), Json(new { title = "Album" })),
            Event(device, 3, "artists", EntityId("LibraryArtists", "UC_1"), Json(new { artist = "Nucci" })),
            Event(device, 4, "savedSearches", EntityId("LibrarySearches", "0"), JsonScalar("voyage")),
            Event(device, 5, "blacklistedPlaylists", EntityId("blacklistedPlaylist", "PL_x"), Json(new { })));

        await using var db = await OpenDbAsync();
        var accountId = await AccountIdAsync(db, device);

        Assert.Equal(1, await db.Events(SyncDomain.Settings).CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Events(SyncDomain.Albums).CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Events(SyncDomain.Artists).CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Events(SyncDomain.SavedSearches).CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await db.Events(SyncDomain.BlacklistedPlaylists).CountAsync(x => x.AccountId == accountId));
        // Nothing leaked into a domain that was not written to.
        Assert.Equal(0, await db.Events(SyncDomain.Favourites).CountAsync(x => x.AccountId == accountId));

        Assert.Equal("Nucci", (await db.Artists.SingleAsync(x => x.AccountId == accountId)).Name);
        Assert.Equal("voyage", (await db.SavedSearches.SingleAsync(x => x.AccountId == accountId)).Query);
    }

    private async Task<CloudDbContext> OpenDbAsync()
    {
        var contexts = fixture.Factory.Services.GetRequiredService<IDbContextFactory<CloudDbContext>>();
        return await contexts.CreateDbContextAsync();
    }

    private static async Task<string> AccountIdAsync(CloudDbContext db, Guid deviceId) =>
        (await db.Devices.SingleAsync(x => x.DeviceId == deviceId)).AccountId;

    private static Task<HttpResponseMessage> SyncAsync(
        TestAccount account, Guid deviceId, long checkpoint, params object[] events) =>
        account.PostAsync("sync", new { deviceId, checkpoint, events });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static object Event(
        Guid deviceId, long sequence, string entityType, string entityId, JsonElement payload,
        string operation = "upsert") =>
        new
        {
            eventId = Guid.NewGuid(),
            deviceSequence = sequence,
            hlcPhysicalMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            hlcLogical = 0,
            entityType,
            entityId,
            operation,
            payload
        };

    /// Mirrors the client's <c>&lt;box&gt;:&lt;base64url(jsonEncode(key))&gt;</c> entity id.
    private static string EntityId(string box, string key)
    {
        var json = JsonSerializer.Serialize(key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{box}:{encoded}";
    }

    private static JsonElement Song(string title) => Json(new
    {
        videoId = VideoId,
        title,
        artists = new[] { new { name = "Relja", id = "UC_relja" } },
        duration = 215,
        thumbnails = new[] { new { url = "https://lh3.googleusercontent.com/cover" } }
    });

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonScalar(string value) => JsonSerializer.SerializeToElement(value);
}
