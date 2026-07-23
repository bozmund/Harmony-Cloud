using System.Text.Json;
using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Sync;

public sealed class SyncService(
    IDbContextFactory<CloudDbContext> contexts,
    CloudOptions options,
    TimeProvider clock)
{
    public async Task<SyncResponse> SyncAsync(
        string accountId, SyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Count > options.MaxEventsPerSync)
            throw new InvalidDataException("event_batch_too_large");
        Validate(request);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var device = await db.Devices.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken)
            ?? throw new InvalidDataException("unknown_device");
        if (device.SyncPaused)
            throw new InvalidDataException("sync_paused");

        var accepted = new List<Guid>();
        foreach (var incoming in request.Events.OrderBy(x => x.DeviceSequence))
        {
            var exists = await db.SyncEvents.AnyAsync(
                x => x.AccountId == accountId && x.EventId == incoming.EventId, cancellationToken);
            if (exists)
            {
                accepted.Add(incoming.EventId);
                continue;
            }
            if (incoming.DeviceSequence <= device.LastSequence)
                throw new InvalidDataException("device_sequence_replayed");

            var physical = Math.Min(incoming.HlcPhysicalMs, now.AddMinutes(5).ToUnixTimeMilliseconds());
            var payload = JsonDocument.Parse(incoming.Payload.GetRawText());
            var entity = new SyncEventEntity
            {
                AccountId = accountId,
                EventId = incoming.EventId,
                DeviceId = request.DeviceId,
                DeviceSequence = incoming.DeviceSequence,
                HlcPhysicalMs = physical,
                HlcLogical = incoming.HlcLogical,
                EntityType = incoming.EntityType,
                EntityId = incoming.EntityId,
                Operation = incoming.Operation,
                Payload = payload,
                ReceivedAt = now
            };
            db.SyncEvents.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            await MergeSnapshotAsync(db, entity, cancellationToken);
            device.LastSequence = incoming.DeviceSequence;
            accepted.Add(incoming.EventId);
        }

        device.LastCheckpoint = Math.Max(device.LastCheckpoint, request.Checkpoint);
        device.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var changeEntities = await db.SyncEvents.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Revision > request.Checkpoint)
            .OrderBy(x => x.Revision)
            .Take(options.MaxEventsPerSync)
            .ToListAsync(cancellationToken);
        var changes = changeEntities.Select(x => new ServerSyncEvent(
            x.Revision, x.EventId, x.DeviceId, x.DeviceSequence, x.HlcPhysicalMs, x.HlcLogical,
            x.EntityType, x.EntityId, x.Operation, x.Payload.RootElement.Clone())).ToList();
        var checkpoint = changes.Count == 0 ? request.Checkpoint : changes[^1].Revision;
        await transaction.CommitAsync(cancellationToken);
        return new SyncResponse(checkpoint, accepted, changes);
    }

    private static async Task MergeSnapshotAsync(
        CloudDbContext db, SyncEventEntity incoming, CancellationToken cancellationToken)
    {
        var snapshot = await db.Snapshots.SingleOrDefaultAsync(
            x => x.AccountId == incoming.AccountId
                && x.EntityType == incoming.EntityType
                && x.EntityId == incoming.EntityId,
            cancellationToken);
        if (snapshot is not null && Compare(snapshot, incoming) >= 0) return;

        if (snapshot is null)
        {
            snapshot = new SnapshotEntity
            {
                AccountId = incoming.AccountId,
                EntityType = incoming.EntityType,
                EntityId = incoming.EntityId,
                Payload = JsonDocument.Parse(incoming.Payload.RootElement.GetRawText())
            };
            db.Snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Payload.Dispose();
            snapshot.Payload = JsonDocument.Parse(incoming.Payload.RootElement.GetRawText());
        }
        snapshot.Revision = incoming.Revision;
        snapshot.HlcPhysicalMs = incoming.HlcPhysicalMs;
        snapshot.HlcLogical = incoming.HlcLogical;
        snapshot.HlcDeviceId = incoming.DeviceId;
        snapshot.Tombstone = incoming.Operation == "delete";
    }

    private static int Compare(SnapshotEntity snapshot, SyncEventEntity incoming)
    {
        var physical = snapshot.HlcPhysicalMs.CompareTo(incoming.HlcPhysicalMs);
        if (physical != 0) return physical;
        var logical = snapshot.HlcLogical.CompareTo(incoming.HlcLogical);
        return logical != 0 ? logical : snapshot.HlcDeviceId.CompareTo(incoming.DeviceId);
    }

    private static void Validate(SyncRequest request)
    {
        if (request.DeviceId == Guid.Empty || request.Checkpoint < 0)
            throw new InvalidDataException("invalid_sync_request");
        foreach (var item in request.Events)
        {
            if (item.EventId == Guid.Empty || item.DeviceSequence <= 0 || item.HlcPhysicalMs <= 0
                || item.HlcLogical < 0 || item.EntityType.Length is < 1 or > 48
                || item.EntityId.Length is < 1 or > 256
                || item.Operation is not ("upsert" or "delete"))
                throw new InvalidDataException("invalid_sync_event");
        }
    }
}
