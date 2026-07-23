using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Persistence;

public sealed class CloudDbContext(DbContextOptions<CloudDbContext> options) : DbContext(options)
{
    public DbSet<SyncEventEntity> SyncEvents => Set<SyncEventEntity>();
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PlaybackCommandEntity> PlaybackCommands => Set<PlaybackCommandEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var events = modelBuilder.Entity<SyncEventEntity>();
        events.ToTable("cloud_sync_events");
        events.HasKey(x => x.Revision);
        events.Property(x => x.Revision).HasColumnName("revision").UseIdentityAlwaysColumn();
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
        events.HasIndex(x => new { x.AccountId, x.DeviceId, x.DeviceSequence }).IsUnique();
        events.HasIndex(x => new { x.AccountId, x.Revision });

        var snapshots = modelBuilder.Entity<SnapshotEntity>();
        snapshots.ToTable("cloud_snapshots");
        snapshots.HasKey(x => new { x.AccountId, x.EntityType, x.EntityId });
        snapshots.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        snapshots.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(48);
        snapshots.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(256);
        snapshots.Property(x => x.Revision).HasColumnName("revision");
        snapshots.Property(x => x.HlcPhysicalMs).HasColumnName("hlc_physical_ms");
        snapshots.Property(x => x.HlcLogical).HasColumnName("hlc_logical");
        snapshots.Property(x => x.HlcDeviceId).HasColumnName("hlc_device_id");
        snapshots.Property(x => x.Tombstone).HasColumnName("tombstone");
        snapshots.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");

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
    }
}
