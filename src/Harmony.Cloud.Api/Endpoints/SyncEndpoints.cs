using Harmony.Cloud.Api.Abstractions;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Security;
using Harmony.Cloud.Api.Sync;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Endpoints;

public static class SyncEndpoints
{
    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder cloud)
    {
        cloud.MapPost("/sync", SyncAsync);
        cloud.MapPost("/sync/pause", PauseAsync);
        return cloud;
    }

    private static async Task<IResult> SyncAsync(
        SyncRequest request, HttpContext context, AccountIdentity identity,
        ISyncService sync, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await sync.SyncAsync(identity.Resolve(context), request, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { code = exception.Message });
        }
    }

    private static async Task<IResult> PauseAsync(
        PauseSyncRequest request, HttpContext context, AccountIdentity identity,
        IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken)
    {
        var accountId = identity.Resolve(context);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var updated = await db.Devices
            .Where(x => x.AccountId == accountId && x.DeviceId == request.DeviceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.SyncPaused, request.Paused)
                .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), cancellationToken);
        return updated == 1 ? Results.NoContent() : Results.NotFound();
    }
}
