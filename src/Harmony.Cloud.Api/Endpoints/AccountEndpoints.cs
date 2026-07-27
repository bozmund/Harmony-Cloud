using Harmony.Cloud.Api.Domain;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder cloud)
    {
        cloud.MapDelete("/account", DeleteAsync);
        return cloud;
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context, AccountIdentity identity,
        IDbContextFactory<CloudDbContext> contexts, CancellationToken cancellationToken)
    {
        var accountId = identity.Resolve(context);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var domain in SyncDomains.All)
            await db.Events(domain).Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Songs.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Settings.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Favourites.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.RecentlyPlayed.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Playlists.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.PlaylistSongs.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Albums.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Artists.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.SavedSearches.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.SearchHistory.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.BlacklistedPlaylists.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Devices.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.PlaybackCommands.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.PlaybackSessions.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }
}
