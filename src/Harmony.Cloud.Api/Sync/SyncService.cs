using Harmony.Cloud.Api.Abstractions;
using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Diagnostics;
using Harmony.Cloud.Api.Domain;
using Harmony.Cloud.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Sync;

public sealed class SyncService(
    IDbContextFactory<CloudDbContext> contexts,
    CloudOptions options,
    TimeProvider clock,
    StateProjector projector,
    CloudMetrics metrics) : ISyncService
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
            // A device on an older build keeps sending domains this build no longer syncs
            // (downloads, lyrics, import staging, the restored queue). Rejecting the batch would
            // wedge that device's sync permanently, so acknowledge and drop instead: its outbox
            // drains and nothing is stored.
            if (!SyncDomains.TryParse(incoming.EntityType, out var domain))
            {
                accepted.Add(incoming.EventId);
                if (incoming.DeviceSequence > device.LastSequence)
                    device.LastSequence = incoming.DeviceSequence;
                continue;
            }

            var events = db.Events(domain);
            if (await events.AnyAsync(
                    x => x.AccountId == accountId && x.EventId == incoming.EventId, cancellationToken))
            {
                accepted.Add(incoming.EventId);
                continue;
            }
            // A device sequence that goes backwards is tolerated rather than rejected. It happens
            // legitimately whenever a device rebuilds its local sync state — a storage-layout
            // change, a restore — while the server still remembers the old high-water mark, and
            // failing the batch wedges that device forever with no way back. Genuine duplicates are
            // already caught above by the unique event id, and nothing downstream orders on this:
            // the log orders by revision and state conflicts resolve on the hybrid clock.
            var physical = Math.Min(incoming.HlcPhysicalMs, now.AddMinutes(5).ToUnixTimeMilliseconds());
            var payload = SyncPayloadPolicy.Normalize(incoming.EntityType, incoming.EntityId, incoming.Payload);
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
            events.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            await projector.ApplyAsync(db, domain, entity, cancellationToken);
            device.LastSequence = Math.Max(device.LastSequence, incoming.DeviceSequence);
            accepted.Add(incoming.EventId);
        }

        device.LastCheckpoint = Math.Max(device.LastCheckpoint, request.Checkpoint);
        device.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var changes = await ReadChangesAsync(db, accountId, request.Checkpoint, cancellationToken);
        var checkpoint = changes.Count == 0 ? request.Checkpoint : changes[^1].Revision;
        await transaction.CommitAsync(cancellationToken);
        metrics.RecordAcceptedSyncEvents(accepted.Count);
        return new SyncResponse(checkpoint, accepted, changes);
    }

    /// <summary>
    /// Merges the per-domain logs back into one revision-ordered feed. Every domain table draws
    /// <c>revision</c> from the same sequence, so ordering across tables is total and a device can
    /// still track its position with a single checkpoint.
    /// </summary>
    private async Task<List<ServerSyncEvent>> ReadChangesAsync(
        CloudDbContext db, string accountId, long checkpoint, CancellationToken cancellationToken)
    {
        var merged = new List<SyncEventEntity>();
        foreach (var domain in SyncDomains.All)
        {
            var slice = await db.Events(domain).AsNoTracking()
                .Where(x => x.AccountId == accountId && x.Revision > checkpoint)
                .OrderBy(x => x.Revision)
                .Take(options.MaxEventsPerSync)
                .ToListAsync(cancellationToken);
            merged.AddRange(slice);
        }

        return merged
            .OrderBy(x => x.Revision)
            .Take(options.MaxEventsPerSync)
            .Select(x => new ServerSyncEvent(
                x.Revision, x.EventId, x.DeviceId, x.DeviceSequence, x.HlcPhysicalMs, x.HlcLogical,
                x.EntityType, x.EntityId, x.Operation, x.Payload.RootElement.Clone()))
            .ToList();
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
