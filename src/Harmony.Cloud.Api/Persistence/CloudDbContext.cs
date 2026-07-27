using Harmony.Cloud.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Persistence;

public sealed class CloudDbContext(DbContextOptions<CloudDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Every per-domain event table draws from one sequence, so <c>revision</c> stays globally
    /// monotonic across domains. That is what lets a device keep a single <c>checkpoint</c> number
    /// and ask for "everything after N" even though the log is physically split.
    /// </summary>
    public const string RevisionSequence = "cloud_revision_seq";

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PlaybackCommandEntity> PlaybackCommands => Set<PlaybackCommandEntity>();
    public DbSet<PlaybackSessionEntity> PlaybackSessions => Set<PlaybackSessionEntity>();

    public DbSet<SongRow> Songs => Set<SongRow>();
    public DbSet<SettingRow> Settings => Set<SettingRow>();
    public DbSet<FavouriteRow> Favourites => Set<FavouriteRow>();
    public DbSet<RecentlyPlayedRow> RecentlyPlayed => Set<RecentlyPlayedRow>();
    public DbSet<PlaylistRow> Playlists => Set<PlaylistRow>();
    public DbSet<PlaylistSongRow> PlaylistSongs => Set<PlaylistSongRow>();
    public DbSet<AlbumRow> Albums => Set<AlbumRow>();
    public DbSet<ArtistRow> Artists => Set<ArtistRow>();
    public DbSet<SavedSearchRow> SavedSearches => Set<SavedSearchRow>();
    public DbSet<SearchHistoryRow> SearchHistory => Set<SearchHistoryRow>();
    public DbSet<BlacklistedPlaylistRow> BlacklistedPlaylists => Set<BlacklistedPlaylistRow>();

    /// <summary>The event log for one domain. See <see cref="SyncDomains.EventEntityName"/>.</summary>
    public DbSet<SyncEventEntity> Events(SyncDomain domain) =>
        Set<SyncEventEntity>(SyncDomains.EventEntityName(domain));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(RevisionSequence);

        foreach (var domain in SyncDomains.All)
        {
            var events = modelBuilder.SharedTypeEntity<SyncEventEntity>(
                SyncDomains.EventEntityName(domain));
            events.ToTable(SyncDomains.EventTableName(domain));
            events.HasKey(x => x.Revision);
            events.Property(x => x.Revision).HasColumnName("revision")
                .HasDefaultValueSql($"nextval('\"{RevisionSequence}\"')");
            events.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
            events.Property(x => x.EventId).HasColumnName("event_id");
            events.Property(x => x.DeviceId).HasColumnName("device_id");
            events.Property(x => x.DeviceSequence).HasColumnName("device_sequence");
            events.Property(x => x.HlcPhysicalMs).HasColumnName("hlc_physical_ms");
            events.Property(x => x.HlcLogical).HasColumnName("hlc_logical");
            events.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(48);
            events.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(256);
            events.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(16);
            events.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            events.Property(x => x.ReceivedAt).HasColumnName("received_at");
            events.HasIndex(x => new { x.AccountId, x.EventId }).IsUnique();
            events.HasIndex(x => new { x.AccountId, x.Revision });
        }

        ConfigureState<SongRow>(modelBuilder, "cloud_songs", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.VideoId });
            entity.Property(x => x.VideoId).HasColumnName("video_id").HasMaxLength(64);
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
            entity.Property(x => x.Artists).HasColumnName("artists").HasColumnType("jsonb");
            entity.Property(x => x.Album).HasColumnName("album").HasColumnType("jsonb");
            entity.Property(x => x.DurationSeconds).HasColumnName("duration_s");
            entity.Property(x => x.ArtworkUrl).HasColumnName("artwork_url").HasMaxLength(1024);
            entity.Property(x => x.Year).HasColumnName("year").HasMaxLength(32);
            entity.Property(x => x.DateMs).HasColumnName("date_ms");
            entity.Property(x => x.TrackDetails).HasColumnName("track_details").HasColumnType("jsonb");
        });

        ConfigureState<SettingRow>(modelBuilder, "cloud_settings", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.Key });
            entity.Property(x => x.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(x => x.Value).HasColumnName("value").HasColumnType("jsonb");
        });

        ConfigureState<FavouriteRow>(modelBuilder, "cloud_favourites", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.VideoId });
            entity.Property(x => x.VideoId).HasColumnName("video_id").HasMaxLength(64);
        });

        ConfigureState<RecentlyPlayedRow>(modelBuilder, "cloud_recently_played", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.EntryKey });
            entity.Property(x => x.EntryKey).HasColumnName("entry_key").HasMaxLength(64);
            entity.Property(x => x.VideoId).HasColumnName("video_id").HasMaxLength(64);
        });

        ConfigureState<PlaylistRow>(modelBuilder, "cloud_playlists", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.PlaylistId });
            entity.Property(x => x.PlaylistId).HasColumnName("playlist_id").HasMaxLength(128);
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(2048);
            entity.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url").HasMaxLength(1024);
            entity.Property(x => x.ItemCount).HasColumnName("item_count").HasMaxLength(32);
            entity.Property(x => x.IsPipedPlaylist).HasColumnName("is_piped_playlist");
            entity.Property(x => x.IsCloudPlaylist).HasColumnName("is_cloud_playlist");
        });

        ConfigureState<PlaylistSongRow>(modelBuilder, "cloud_playlist_songs", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.PlaylistId, x.EntryKey });
            entity.Property(x => x.PlaylistId).HasColumnName("playlist_id").HasMaxLength(128);
            entity.Property(x => x.EntryKey).HasColumnName("entry_key").HasMaxLength(64);
            entity.Property(x => x.VideoId).HasColumnName("video_id").HasMaxLength(64);
        });

        ConfigureState<AlbumRow>(modelBuilder, "cloud_albums", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.BrowseId });
            entity.Property(x => x.BrowseId).HasColumnName("browse_id").HasMaxLength(128);
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
            entity.Property(x => x.Artists).HasColumnName("artists").HasColumnType("jsonb");
            entity.Property(x => x.Year).HasColumnName("year").HasMaxLength(32);
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(2048);
            entity.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url").HasMaxLength(1024);
            entity.Property(x => x.AudioPlaylistId).HasColumnName("audio_playlist_id").HasMaxLength(128);
        });

        ConfigureState<ArtistRow>(modelBuilder, "cloud_artists", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.BrowseId });
            entity.Property(x => x.BrowseId).HasColumnName("browse_id").HasMaxLength(128);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
            entity.Property(x => x.RadioId).HasColumnName("radio_id").HasMaxLength(128);
            entity.Property(x => x.Subscribers).HasColumnName("subscribers").HasMaxLength(64);
            entity.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url").HasMaxLength(1024);
        });

        ConfigureState<SavedSearchRow>(modelBuilder, "cloud_saved_searches", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.EntryKey });
            entity.Property(x => x.EntryKey).HasColumnName("entry_key").HasMaxLength(64);
            entity.Property(x => x.Query).HasColumnName("query").HasMaxLength(512);
        });

        ConfigureState<SearchHistoryRow>(modelBuilder, "cloud_search_history", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.EntryKey });
            entity.Property(x => x.EntryKey).HasColumnName("entry_key").HasMaxLength(64);
            entity.Property(x => x.Query).HasColumnName("query").HasMaxLength(512);
        });

        ConfigureState<BlacklistedPlaylistRow>(modelBuilder, "cloud_blacklisted_playlists", entity =>
        {
            entity.HasKey(x => new { x.AccountId, x.PlaylistId });
            entity.Property(x => x.PlaylistId).HasColumnName("playlist_id").HasMaxLength(128);
            entity.Property(x => x.Details).HasColumnName("details").HasColumnType("jsonb");
        });

        var devices = modelBuilder.Entity<DeviceEntity>();
        devices.ToTable("cloud_devices");
        devices.HasKey(x => new { x.AccountId, x.DeviceId });
        devices.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        devices.Property(x => x.DeviceId).HasColumnName("device_id");
        devices.Property(x => x.Name).HasColumnName("name").HasMaxLength(80);
        devices.Property(x => x.Platform).HasColumnName("platform").HasMaxLength(24);
        devices.Property(x => x.AppVersion).HasColumnName("app_version").HasMaxLength(80);
        devices.Property(x => x.PushTokenCiphertext).HasColumnName("push_token_ciphertext").HasMaxLength(8192);
        devices.Property(x => x.PushRegisteredAt).HasColumnName("push_registered_at");
        devices.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        devices.Property(x => x.IsRealtimeConnected).HasColumnName("is_realtime_connected");
        devices.Property(x => x.LastSequence).HasColumnName("last_sequence");
        devices.Property(x => x.LastCheckpoint).HasColumnName("last_checkpoint");
        devices.Property(x => x.SyncPaused).HasColumnName("sync_paused");
        devices.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        var commands = modelBuilder.Entity<PlaybackCommandEntity>();
        commands.ToTable("cloud_playback_commands");
        commands.HasKey(x => new { x.AccountId, x.CommandId });
        commands.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        commands.Property(x => x.CommandId).HasColumnName("command_id");
        commands.Property(x => x.SourceDeviceId).HasColumnName("source_device_id");
        commands.Property(x => x.TargetDeviceId).HasColumnName("target_device_id");
        commands.Property(x => x.Type).HasColumnName("type").HasMaxLength(48);
        commands.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        commands.Property(x => x.CreatedAt).HasColumnName("created_at");
        commands.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        commands.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
        commands.Property(x => x.Applied).HasColumnName("applied");
        commands.HasIndex(x => new { x.AccountId, x.TargetDeviceId, x.ExpiresAt });

        var sessions = modelBuilder.Entity<PlaybackSessionEntity>();
        sessions.ToTable("cloud_playback_sessions");
        sessions.HasKey(x => new { x.AccountId, x.SessionId });
        sessions.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        sessions.Property(x => x.SessionId).HasColumnName("session_id");
        sessions.Property(x => x.TargetDeviceId).HasColumnName("target_device_id");
        sessions.Property(x => x.State).HasColumnName("state").HasColumnType("jsonb");
        sessions.Property(x => x.Sequence).HasColumnName("sequence");
        sessions.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        sessions.Property(x => x.EndedAt).HasColumnName("ended_at");
        sessions.Property(x => x.CurrentSongId).HasColumnName("current_song_id").HasMaxLength(64);
        sessions.Property(x => x.PositionMs).HasColumnName("position_ms");
        sessions.Property(x => x.DurationMs).HasColumnName("duration_ms");
        sessions.Property(x => x.Playing).HasColumnName("playing");
        sessions.Property(x => x.ProgressUpdatedAt).HasColumnName("progress_updated_at");
        sessions.HasIndex(x => new { x.AccountId, x.EndedAt });
    }

    private static void ConfigureState<TRow>(
        ModelBuilder modelBuilder,
        string tableName,
        Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TRow>> configure)
        where TRow : StateRow
    {
        var entity = modelBuilder.Entity<TRow>();
        entity.ToTable(tableName);
        entity.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        entity.Property(x => x.Revision).HasColumnName("revision");
        entity.Property(x => x.HlcPhysicalMs).HasColumnName("hlc_physical_ms");
        entity.Property(x => x.HlcLogical).HasColumnName("hlc_logical");
        entity.Property(x => x.HlcDeviceId).HasColumnName("hlc_device_id");
        entity.Property(x => x.Tombstone).HasColumnName("tombstone");
        configure(entity);
    }
}
