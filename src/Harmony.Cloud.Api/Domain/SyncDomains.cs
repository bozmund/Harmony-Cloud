namespace Harmony.Cloud.Api.Domain;

/// <summary>
/// The kinds of data an account syncs. Each one owns its own event table and its own state
/// table(s), so a theme colour is never stored alongside a liked song.
///
/// The wire value is what a device sends as <c>entityType</c>. Anything not listed here is
/// deliberately not synced — downloads, the liked-not-downloaded view, the restored playback
/// queue, import staging and the lyrics cache are all device-local.
/// </summary>
public enum SyncDomain
{
    Settings,
    Favourites,
    RecentlyPlayed,
    Playlists,
    PlaylistSongs,
    Albums,
    Artists,
    SavedSearches,
    SearchHistory,
    BlacklistedPlaylists
}

public static class SyncDomains
{
    /// <summary>Wire value -> domain. Matched case-insensitively.</summary>
    private static readonly Dictionary<string, SyncDomain> ByWireValue =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = SyncDomain.Settings,
            ["favourites"] = SyncDomain.Favourites,
            ["recentlyPlayed"] = SyncDomain.RecentlyPlayed,
            ["playlists"] = SyncDomain.Playlists,
            ["playlistSongs"] = SyncDomain.PlaylistSongs,
            ["albums"] = SyncDomain.Albums,
            ["artists"] = SyncDomain.Artists,
            ["savedSearches"] = SyncDomain.SavedSearches,
            ["searchHistory"] = SyncDomain.SearchHistory,
            ["blacklistedPlaylists"] = SyncDomain.BlacklistedPlaylists
        };

    /// <summary>
    /// EF shared-type entity names for the per-domain event tables. One
    /// <see cref="Persistence.SyncEventEntity"/> CLR type is mapped once per domain rather than
    /// duplicating ten near-identical classes.
    /// </summary>
    private static readonly Dictionary<SyncDomain, string> EventEntityNames = new()
    {
        [SyncDomain.Settings] = "SettingsEvent",
        [SyncDomain.Favourites] = "FavouritesEvent",
        [SyncDomain.RecentlyPlayed] = "RecentlyPlayedEvent",
        [SyncDomain.Playlists] = "PlaylistsEvent",
        [SyncDomain.PlaylistSongs] = "PlaylistSongsEvent",
        [SyncDomain.Albums] = "AlbumsEvent",
        [SyncDomain.Artists] = "ArtistsEvent",
        [SyncDomain.SavedSearches] = "SavedSearchesEvent",
        [SyncDomain.SearchHistory] = "SearchHistoryEvent",
        [SyncDomain.BlacklistedPlaylists] = "BlacklistedPlaylistsEvent"
    };

    private static readonly Dictionary<SyncDomain, string> TableNames = new()
    {
        [SyncDomain.Settings] = "cloud_events_settings",
        [SyncDomain.Favourites] = "cloud_events_favourites",
        [SyncDomain.RecentlyPlayed] = "cloud_events_recently_played",
        [SyncDomain.Playlists] = "cloud_events_playlists",
        [SyncDomain.PlaylistSongs] = "cloud_events_playlist_songs",
        [SyncDomain.Albums] = "cloud_events_albums",
        [SyncDomain.Artists] = "cloud_events_artists",
        [SyncDomain.SavedSearches] = "cloud_events_saved_searches",
        [SyncDomain.SearchHistory] = "cloud_events_search_history",
        [SyncDomain.BlacklistedPlaylists] = "cloud_events_blacklisted_playlists"
    };

    public static IReadOnlyCollection<SyncDomain> All => EventEntityNames.Keys;

    /// <summary>
    /// Resolves the domain a device's <c>entityType</c> refers to. Returns false for anything
    /// unknown — including the domains this build no longer syncs, which older app builds keep
    /// sending until they are updated. Callers must accept and drop those rather than failing the
    /// batch, or the device's outbox can never drain.
    /// </summary>
    public static bool TryParse(string entityType, out SyncDomain domain) =>
        ByWireValue.TryGetValue(entityType, out domain);

    public static string EventEntityName(SyncDomain domain) => EventEntityNames[domain];

    public static string EventTableName(SyncDomain domain) => TableNames[domain];
}
