using System.Text.Json;
using Harmony.Cloud.Api.Domain;
using Harmony.Cloud.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Sync;

/// <summary>
/// Projects an accepted event onto the typed state tables. The event log stays the source of truth
/// and is what serves <c>changes</c>; these tables are the readable current state of an account.
///
/// Ordering is decided by the same hybrid clock the previous single snapshot table used, so a late
/// arrival from a device with an older clock never overwrites a newer write.
/// </summary>
public sealed class StateProjector
{
    public async Task ApplyAsync(
        CloudDbContext db, SyncDomain domain, SyncEventEntity incoming, CancellationToken cancellationToken)
    {
        var key = EntityKey.Parse(incoming.EntityId);
        var deleted = incoming.Operation == "delete";
        var payload = incoming.Payload.RootElement;

        switch (domain)
        {
            case SyncDomain.Settings:
                await UpsertAsync(db.Settings, x => x.AccountId == incoming.AccountId && x.Key == key.Id,
                    () => new SettingRow
                    {
                        AccountId = incoming.AccountId,
                        Key = key.Id,
                        Value = EmptyObject()
                    },
                    row => row.Value = deleted ? EmptyObject() : Clone(payload),
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.Favourites:
                await UpsertSongAsync(db, incoming, payload, deleted, cancellationToken);
                await UpsertAsync(db.Favourites, x => x.AccountId == incoming.AccountId && x.VideoId == key.Id,
                    () => new FavouriteRow { AccountId = incoming.AccountId, VideoId = key.Id },
                    _ => { },
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.RecentlyPlayed:
                await UpsertSongAsync(db, incoming, payload, deleted, cancellationToken);
                await UpsertAsync(db.RecentlyPlayed, x => x.AccountId == incoming.AccountId && x.EntryKey == key.Id,
                    () => new RecentlyPlayedRow { AccountId = incoming.AccountId, EntryKey = key.Id },
                    row => row.VideoId = Text(payload, "videoId"),
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.PlaylistSongs:
                await UpsertSongAsync(db, incoming, payload, deleted, cancellationToken);
                await UpsertAsync(db.PlaylistSongs,
                    x => x.AccountId == incoming.AccountId && x.PlaylistId == key.Container && x.EntryKey == key.Id,
                    () => new PlaylistSongRow
                    {
                        AccountId = incoming.AccountId,
                        PlaylistId = key.Container,
                        EntryKey = key.Id
                    },
                    row => row.VideoId = Text(payload, "videoId"),
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.Playlists:
                await UpsertAsync(db.Playlists, x => x.AccountId == incoming.AccountId && x.PlaylistId == key.Id,
                    () => new PlaylistRow { AccountId = incoming.AccountId, PlaylistId = key.Id },
                    row =>
                    {
                        row.Title = Text(payload, "title");
                        row.Description = Text(payload, "description");
                        row.ThumbnailUrl = FirstThumbnailUrl(payload);
                        row.ItemCount = Text(payload, "itemCount");
                        row.IsPipedPlaylist = Flag(payload, "isPipedPlaylist");
                        row.IsCloudPlaylist = Flag(payload, "isCloudPlaylist");
                    },
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.Albums:
                await UpsertAsync(db.Albums, x => x.AccountId == incoming.AccountId && x.BrowseId == key.Id,
                    () => new AlbumRow { AccountId = incoming.AccountId, BrowseId = key.Id },
                    row =>
                    {
                        row.Title = Text(payload, "title");
                        row.Artists = CloneOrNull(payload, "artists");
                        row.Year = Text(payload, "year");
                        row.Description = Text(payload, "description");
                        row.ThumbnailUrl = FirstThumbnailUrl(payload);
                        row.AudioPlaylistId = Text(payload, "audioPlaylistId");
                    },
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.Artists:
                await UpsertAsync(db.Artists, x => x.AccountId == incoming.AccountId && x.BrowseId == key.Id,
                    () => new ArtistRow { AccountId = incoming.AccountId, BrowseId = key.Id },
                    row =>
                    {
                        row.Name = Text(payload, "artist") ?? Text(payload, "name");
                        row.RadioId = Text(payload, "radioId");
                        row.Subscribers = Text(payload, "subscribers");
                        row.ThumbnailUrl = FirstThumbnailUrl(payload);
                    },
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.SavedSearches:
                await UpsertAsync(db.SavedSearches, x => x.AccountId == incoming.AccountId && x.EntryKey == key.Id,
                    () => new SavedSearchRow { AccountId = incoming.AccountId, EntryKey = key.Id },
                    row => row.Query = Scalar(payload),
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.SearchHistory:
                await UpsertAsync(db.SearchHistory, x => x.AccountId == incoming.AccountId && x.EntryKey == key.Id,
                    () => new SearchHistoryRow { AccountId = incoming.AccountId, EntryKey = key.Id },
                    row => row.Query = Scalar(payload),
                    incoming, deleted, cancellationToken);
                break;

            case SyncDomain.BlacklistedPlaylists:
                await UpsertAsync(db.BlacklistedPlaylists,
                    x => x.AccountId == incoming.AccountId && x.PlaylistId == key.Id,
                    () => new BlacklistedPlaylistRow { AccountId = incoming.AccountId, PlaylistId = key.Id },
                    row => row.Details = deleted ? null : Clone(payload),
                    incoming, deleted, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Writes the shared song row referenced by favourites, recently played and playlist contents,
    /// so metadata lives once per account instead of once per list that mentions the song. A delete
    /// removes the membership, never the song itself — other lists may still reference it.
    /// </summary>
    private static async Task UpsertSongAsync(
        CloudDbContext db, SyncEventEntity incoming, JsonElement payload, bool deleted,
        CancellationToken cancellationToken)
    {
        if (deleted) return;
        var videoId = Text(payload, "videoId");
        if (string.IsNullOrEmpty(videoId)) return;

        await UpsertAsync(db.Songs, x => x.AccountId == incoming.AccountId && x.VideoId == videoId,
            () => new SongRow { AccountId = incoming.AccountId, VideoId = videoId },
            row =>
            {
                row.Title = Text(payload, "title");
                row.Artists = CloneOrNull(payload, "artists");
                row.Album = CloneOrNull(payload, "album");
                row.DurationSeconds = Number(payload, "duration");
                row.ArtworkUrl = FirstThumbnailUrl(payload);
                row.Year = Text(payload, "year");
                row.DateMs = LongNumber(payload, "date");
                row.TrackDetails = CloneOrNull(payload, "trackDetails");
            },
            incoming, tombstone: false, cancellationToken);
    }

    private static async Task UpsertAsync<TRow>(
        DbSet<TRow> set,
        System.Linq.Expressions.Expression<Func<TRow, bool>> match,
        Func<TRow> create,
        Action<TRow> update,
        SyncEventEntity incoming,
        bool tombstone,
        CancellationToken cancellationToken)
        where TRow : StateRow
    {
        var row = await set.SingleOrDefaultAsync(match, cancellationToken);
        if (row is not null && Compare(row, incoming) >= 0) return;
        if (row is null)
        {
            row = create();
            set.Add(row);
        }

        update(row);
        row.Revision = incoming.Revision;
        row.HlcPhysicalMs = incoming.HlcPhysicalMs;
        row.HlcLogical = incoming.HlcLogical;
        row.HlcDeviceId = incoming.DeviceId;
        row.Tombstone = tombstone;
    }

    private static int Compare(StateRow row, SyncEventEntity incoming)
    {
        var physical = row.HlcPhysicalMs.CompareTo(incoming.HlcPhysicalMs);
        if (physical != 0) return physical;
        var logical = row.HlcLogical.CompareTo(incoming.HlcLogical);
        return logical != 0 ? logical : row.HlcDeviceId.CompareTo(incoming.DeviceId);
    }

    private static JsonDocument EmptyObject() => JsonDocument.Parse("{}");

    private static JsonDocument Clone(JsonElement element) => JsonDocument.Parse(element.GetRawText());

    private static JsonDocument? CloneOrNull(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? Clone(value)
            : null;

    private static string? Text(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True or JsonValueKind.False => value.ToString(),
                _ => null
            }
            : null;

    /// <summary>Search entries are bare strings rather than objects.</summary>
    private static string? Scalar(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.String ? payload.GetString() : null;

    private static bool Flag(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static int? Number(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? LongNumber(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    /// <summary>
    /// Artwork is stored as <c>thumbnails: [{ url }]</c> across songs, playlists, albums and
    /// artists. Tolerates every degraded shape seen on real data, including a list holding an
    /// empty map written by older clients.
    /// </summary>
    private static string? FirstThumbnailUrl(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty("thumbnails", out var thumbnails)) return null;
        if (thumbnails.ValueKind != JsonValueKind.Array) return null;
        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            if (thumbnail.ValueKind != JsonValueKind.Object) continue;
            if (!thumbnail.TryGetProperty("url", out var url)) continue;
            if (url.ValueKind != JsonValueKind.String) continue;
            var text = url.GetString();
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return null;
    }
}

/// <summary>
/// A device's <c>entityId</c> is <c>&lt;HiveBox&gt;:&lt;base64url(jsonEncode(key))&gt;</c>. The box
/// segment doubles as the container for per-playlist song boxes, whose name is the playlist id.
/// </summary>
public readonly record struct EntityKey(string Container, string Id)
{
    public static EntityKey Parse(string entityId)
    {
        var separator = entityId.IndexOf(':');
        if (separator <= 0) return new EntityKey(string.Empty, entityId);

        var container = entityId[..separator];
        var encoded = entityId[(separator + 1)..];
        return new EntityKey(container, Decode(encoded) ?? encoded);
    }

    private static string? Decode(string encoded)
    {
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Number => document.RootElement.ToString(),
                _ => null
            };
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}
