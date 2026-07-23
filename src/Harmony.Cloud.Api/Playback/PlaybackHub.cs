using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Playback;

[Authorize]
public sealed class PlaybackHub(AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, TimeProvider clock) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var rawId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();
        if (!Guid.TryParse(rawId, out var deviceId)) throw new HubException("invalid_device");
        var accountId = identity.Resolve(Context.GetHttpContext()!);
        await using var db = await contexts.CreateDbContextAsync(Context.ConnectionAborted);
        var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == deviceId, Context.ConnectionAborted);
        if (device is null) throw new HubException("unknown_device");
        device.IsRealtimeConnected = true;
        device.LastSeenAt = clock.GetUtcNow();
        await db.SaveChangesAsync(Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(accountId, deviceId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var rawId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();
        if (Guid.TryParse(rawId, out var deviceId))
        {
            var accountId = identity.Resolve(Context.GetHttpContext()!);
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == deviceId);
            if (device is not null)
            {
                device.IsRealtimeConnected = false;
                device.LastSeenAt = clock.GetUtcNow();
                await db.SaveChangesAsync();
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static string Group(string accountId, Guid deviceId) => $"playback:{accountId}:{deviceId:N}";
}
