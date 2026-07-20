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

public sealed class SnapshotEntity
{
    public required string AccountId { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public long Revision { get; set; }
    public long HlcPhysicalMs { get; set; }
    public int HlcLogical { get; set; }
    public Guid HlcDeviceId { get; set; }
    public bool Tombstone { get; set; }
    public required JsonDocument Payload { get; set; }
}

public sealed class DeviceEntity
{
    public required string AccountId { get; set; }
    public Guid DeviceId { get; set; }
    public required string Name { get; set; }
    public long LastSequence { get; set; }
    public long LastCheckpoint { get; set; }
    public bool SyncPaused { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
