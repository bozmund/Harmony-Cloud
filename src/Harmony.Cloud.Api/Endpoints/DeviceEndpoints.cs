using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Playback;
using Harmony.Cloud.Api.Security;
using Harmony.Cloud.Api.Sync;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Endpoints;

public static class DeviceEndpoints
{
    public static RouteGroupBuilder MapDeviceEndpoints(this RouteGroupBuilder cloud)
    {
        cloud.MapPost("/devices/register", RegisterAsync);
        cloud.MapPost("/playback/presence", RecordPresenceAsync);
        cloud.MapPut("/playback/push-registration", RegisterPushAsync);
        return cloud;
    }

    private static async Task<IResult> RegisterAsync(RegisterDeviceRequest request, HttpContext context, AccountIdentity identity,
        IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80)
            return Results.BadRequest(new { code = "invalid_device" });
        var accountId = identity.Resolve(context);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken);
        if (device is null)
        {
            device = new DeviceEntity { AccountId = accountId, DeviceId = request.DeviceId, Name = request.Name.Trim() };
            db.Devices.Add(device);
        }
        else device.Name = request.Name.Trim();
        device.Platform = Normalize(request.Platform, 24, "unknown", lowercase: true);
        device.AppVersion = Normalize(request.AppVersion, 80, "unknown", lowercase: false);
        device.UpdatedAt = clock.GetUtcNow();
        device.LastSeenAt = device.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { request.DeviceId });
    }

    private static async Task<IResult> RecordPresenceAsync(DevicePresenceRequest request, HttpContext context, AccountIdentity identity,
        IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken)
    {
        var accountId = identity.Resolve(context);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken);
        if (device is null) return Results.NotFound();
        device.LastSeenAt = clock.GetUtcNow();
        device.UpdatedAt = device.LastSeenAt.Value;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RegisterPushAsync(PushRegistrationRequest request, HttpContext context, AccountIdentity identity,
        IDbContextFactory<CloudDbContext> contexts, FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, "fcm", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096)
            return Results.BadRequest(new { code = "invalid_push_registration" });
        var accountId = identity.Resolve(context);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken);
        if (device is null) return Results.NotFound();
        device.PushTokenCiphertext = fcm.Protect(request.Token);
        device.PushRegisteredAt = clock.GetUtcNow();
        device.LastSeenAt = device.PushRegisteredAt;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static string Normalize(string value, int maximumLength, string fallback, bool lowercase)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.Trim()[..Math.Min(maximumLength, value.Trim().Length)];
        return lowercase ? normalized.ToLowerInvariant() : normalized;
    }
}
