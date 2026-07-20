using System.Text.Json;

namespace Harmony.Cloud.Api.Sync;

public sealed record RegisterDeviceRequest(Guid DeviceId, string Name);
public sealed record SyncRequest(Guid DeviceId, long Checkpoint, IReadOnlyList<ClientSyncEvent> Events);
public sealed record ClientSyncEvent(
    Guid EventId,
    long DeviceSequence,
    long HlcPhysicalMs,
    int HlcLogical,
    string EntityType,
    string EntityId,
    string Operation,
    JsonElement Payload);
public sealed record ServerSyncEvent(
    long Revision,
    Guid EventId,
    Guid DeviceId,
    long DeviceSequence,
    long HlcPhysicalMs,
    int HlcLogical,
    string EntityType,
    string EntityId,
    string Operation,
    JsonElement Payload);
public sealed record SyncResponse(long Checkpoint, IReadOnlyList<Guid> AcceptedEventIds, IReadOnlyList<ServerSyncEvent> Changes);
public sealed record PauseSyncRequest(Guid DeviceId, bool Paused);
