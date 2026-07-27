using System.Text.Json;

namespace Harmony.Cloud.Api.Persistence;

/// <summary>
/// Last-write-wins bookkeeping carried by every state row, mirroring what the old single
/// <c>cloud_snapshots</c> table tracked: the hybrid clock that decides which device's write wins,
/// and a tombstone so a delete is not resurrected by a late-arriving older upsert.
/// </summary>
public abstract class StateRow
{
    public required string AccountId { get; set; }
    public long Revision { get; set; }
    public long HlcPhysicalMs { get; set; }
    public int HlcLogical { get; set; }
    public Guid HlcDeviceId { get; set; }
    public bool Tombstone { get; set; }
}

/// <summary>
/// One row per song per account, shared by every domain that references it. Favourites, recently
/// played and playlist contents hold only a video id, so a song's metadata lives in exactly one
/// place instead of being copied into each list that mentions it.
/// </summary>
public sealed class SongRow : StateRow
{
    public required string VideoId { get; set; }
    public string? Title { get; set; }
    public JsonDocument? Artists { get; set; }
    public JsonDocument? Album { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? Year { get; set; }
    public long? DateMs { get; set; }
    public JsonDocument? TrackDetails { get; set; }
}

public sealed class SettingRow : StateRow
{
    public required string Key { get; set; }
    public required JsonDocument Value { get; set; }
}

public sealed class FavouriteRow : StateRow
{
    public required string VideoId { get; set; }
}

/// <summary>
/// Recently played and playlist contents are ordered Hive lists keyed by an auto-incrementing
/// integer rather than by song, so the entry key is part of the identity: the same song can appear
/// twice, and position is what the client round-trips.
/// </summary>
public sealed class RecentlyPlayedRow : StateRow
{
    public required string EntryKey { get; set; }
    public string? VideoId { get; set; }
}

public sealed class PlaylistSongRow : StateRow
{
    public required string PlaylistId { get; set; }
    public required string EntryKey { get; set; }
    public string? VideoId { get; set; }
}

public sealed class PlaylistRow : StateRow
{
    public required string PlaylistId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ItemCount { get; set; }
    public bool IsPipedPlaylist { get; set; }
    public bool IsCloudPlaylist { get; set; }
}

public sealed class AlbumRow : StateRow
{
    public required string BrowseId { get; set; }
    public string? Title { get; set; }
    public JsonDocument? Artists { get; set; }
    public string? Year { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? AudioPlaylistId { get; set; }
}

public sealed class ArtistRow : StateRow
{
    public required string BrowseId { get; set; }
    public string? Name { get; set; }
    public string? RadioId { get; set; }
    public string? Subscribers { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public sealed class SavedSearchRow : StateRow
{
    public required string EntryKey { get; set; }
    public string? Query { get; set; }
}

public sealed class SearchHistoryRow : StateRow
{
    public required string EntryKey { get; set; }
    public string? Query { get; set; }
}

public sealed class BlacklistedPlaylistRow : StateRow
{
    public required string PlaylistId { get; set; }
    public JsonDocument? Details { get; set; }
}
