using System.Text.Json;

namespace Harmony.Cloud.Api.Persistence;

public sealed class SyncEventEntity
{
    public long Revision { get; set; }
    public required string AccountId { get; set; }
    public Guid EventId { get; set; }
    public Guid DeviceId { get; set; }
    public long DeviceSequence { get; set; }
    public long HlcPhysicalMs { get; set; }
    public int HlcLogical { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Operation { get; set; }
    public required JsonDocument Payload { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class DeviceEntity
{
    public required string AccountId { get; set; }
    public Guid DeviceId { get; set; }
    public required string Name { get; set; }
    public string Platform { get; set; } = "unknown";
    public string AppVersion { get; set; } = "unknown";
    public string? PushTokenCiphertext { get; set; }
    public DateTimeOffset? PushRegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsRealtimeConnected { get; set; }
    public long LastSequence { get; set; }
    public long LastCheckpoint { get; set; }
    public bool SyncPaused { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PlaybackCommandEntity
{
    public required string AccountId { get; set; }
    public Guid CommandId { get; set; }
    public Guid SourceDeviceId { get; set; }
    public Guid TargetDeviceId { get; set; }
    public required string Type { get; set; }
    public required JsonDocument Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public bool Applied { get; set; }
}

public sealed class PlaybackSessionEntity
{
    public required string AccountId { get; set; }
    public Guid SessionId { get; set; }
    public Guid TargetDeviceId { get; set; }
    /// <summary>
    /// Durable session state: ordered <c>queueIds</c>, index, and modes. Written only when it
    /// actually changes, guarded by <c>queueRevision</c> inside the document.
    /// </summary>
    public required JsonDocument State { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    // Ephemeral playback progress. Broadcast over the socket on every tick and persisted only
    // periodically (see PlaybackSocketEndpoint.ProgressPersistInterval) so a device that joins late
    // still opens on a sensible position instead of zero.
    public string? CurrentSongId { get; set; }
    public long PositionMs { get; set; }
    public long? DurationMs { get; set; }
    public bool Playing { get; set; }
    public DateTimeOffset? ProgressUpdatedAt { get; set; }
}
