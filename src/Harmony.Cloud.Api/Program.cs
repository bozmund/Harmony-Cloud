using Harmony.Cloud.Api.Audio;
using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Playback;
using Harmony.Cloud.Api.Security;
using Harmony.Cloud.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("Cloud").Get<CloudOptions>()
    ?? throw new InvalidOperationException("Cloud configuration is required.");
if (options.IdentityHmacKey.Length < 32)
    throw new InvalidOperationException("Cloud:IdentityHmacKey must contain at least 32 characters.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountIdentity>();
builder.Services.AddSingleton<FcmWakeupService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddHttpClient<ResolverBackupClient>();
builder.Services.AddPooledDbContextFactory<CloudDbContext>(db =>
    db.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("PostgreSql connection is required.")));

var authDomain = builder.Configuration["Auth0:Domain"];
var audience = builder.Configuration["Auth0:Audience"];
var authEnabled = !string.IsNullOrWhiteSpace(authDomain) && !string.IsNullOrWhiteSpace(audience);
if (authEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt =>
    {
        jwt.Authority = $"https://{authDomain!.TrimEnd('/')}/";
        jwt.Audience = audience;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });
builder.Services.AddAuthorization();
}
builder.Services.AddSignalR();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CloudDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    // Initial deployment creates the isolated Harmony Cloud database from this model. Subsequent
    // schema changes must add explicit EF migrations before production rollout.
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS platform varchar(24) NOT NULL DEFAULT 'unknown';
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS app_version varchar(80) NOT NULL DEFAULT 'unknown';
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS push_token_ciphertext varchar(8192);
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS push_registered_at timestamp with time zone;
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS last_seen_at timestamp with time zone;
        ALTER TABLE cloud_devices ADD COLUMN IF NOT EXISTS is_realtime_connected boolean NOT NULL DEFAULT false;
        CREATE TABLE IF NOT EXISTS cloud_playback_commands (
          account_id varchar(64) NOT NULL, command_id uuid NOT NULL, source_device_id uuid NOT NULL,
          target_device_id uuid NOT NULL, type varchar(48) NOT NULL, payload jsonb NOT NULL,
          created_at timestamp with time zone NOT NULL, expires_at timestamp with time zone NOT NULL,
          acknowledged_at timestamp with time zone NULL, applied boolean NOT NULL DEFAULT false,
          PRIMARY KEY (account_id, command_id));
        CREATE INDEX IF NOT EXISTS ix_cloud_playback_commands_target_expiry ON cloud_playback_commands(account_id, target_device_id, expires_at);
        CREATE TABLE IF NOT EXISTS cloud_playback_sessions (
          account_id varchar(64) NOT NULL, session_id uuid NOT NULL, target_device_id uuid NOT NULL,
          state jsonb NOT NULL, sequence bigint NOT NULL DEFAULT 0,
          updated_at timestamp with time zone NOT NULL, ended_at timestamp with time zone NULL,
          PRIMARY KEY (account_id, session_id));
        CREATE INDEX IF NOT EXISTS ix_cloud_playback_sessions_active ON cloud_playback_sessions(account_id, ended_at);
        """);
    return;
}
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/cloud/health/live", () => Results.Ok(new { status = "live" }));
var cloud = app.MapGroup("/cloud/v1");
if (authEnabled) cloud.RequireAuthorization();

cloud.MapPost("/devices/register", async (
    RegisterDeviceRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80)
        return Results.BadRequest(new { code = "invalid_device" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var device = await db.Devices.SingleOrDefaultAsync(
        x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken);
    if (device is null)
    {
        device = new DeviceEntity { AccountId = accountId, DeviceId = request.DeviceId, Name = request.Name.Trim() };
        db.Devices.Add(device);
    }
    else device.Name = request.Name.Trim();
    device.Platform = string.IsNullOrWhiteSpace(request.Platform) ? "unknown" : request.Platform.Trim().ToLowerInvariant()[..Math.Min(24, request.Platform.Trim().Length)];
    device.AppVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? "unknown" : request.AppVersion.Trim()[..Math.Min(80, request.AppVersion.Trim().Length)];
    device.UpdatedAt = clock.GetUtcNow();
    device.LastSeenAt = device.UpdatedAt;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { request.DeviceId });
});

cloud.MapGet("/playback/devices", async (HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, Guid currentDeviceId, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    var connectedCutoff = clock.GetUtcNow() - TimeSpan.FromMinutes(2);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var targetDeviceId = await db.PlaybackSessions.AsNoTracking()
        .Where(x => x.AccountId == accountId && x.EndedAt == null)
        .OrderByDescending(x => x.UpdatedAt).Select(x => (Guid?)x.TargetDeviceId).FirstOrDefaultAsync(cancellationToken);
    var devices = await db.Devices.AsNoTracking().Where(x => x.AccountId == accountId)
        .OrderBy(x => x.Name).Select(x => new PlaybackDeviceResponse(x.DeviceId, x.Name, x.Platform, x.AppVersion,
            x.IsRealtimeConnected || x.LastSeenAt >= connectedCutoff ? "online" : x.PushTokenCiphertext != null ? "background" : "unavailable",
            x.DeviceId == currentDeviceId, targetDeviceId == x.DeviceId)).ToListAsync(cancellationToken);
    return Results.Ok(devices);
});

cloud.MapPost("/playback/presence", async (DevicePresenceRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var device = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.DeviceId, cancellationToken);
    if (device is null) return Results.NotFound();
    device.LastSeenAt = clock.GetUtcNow();
    device.UpdatedAt = device.LastSeenAt.Value;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

cloud.MapPut("/playback/push-registration", async (PushRegistrationRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
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
});

cloud.MapPost("/playback/commands", async (PlaybackCommandRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, IHubContext<PlaybackHub> hub, FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty || request.SourceDeviceId == request.TargetDeviceId ||
        string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 48 || !IsPortablePlaybackPayload(request.Payload))
        return Results.BadRequest(new { code = "invalid_playback_command" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId, cancellationToken))
        return Results.NotFound();
    var target = await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId, cancellationToken);
    if (target is null) return Results.NotFound();
    var now = clock.GetUtcNow();
    var command = new PlaybackCommandEntity { AccountId = accountId, CommandId = Guid.NewGuid(), SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId, Type = request.Type, Payload = JsonDocument.Parse(request.Payload.GetRawText()), CreatedAt = now, ExpiresAt = now.AddMinutes(1) };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.Group(PlaybackHub.Group(accountId, request.TargetDeviceId)).SendAsync("playbackCommandAvailable", command.CommandId, cancellationToken);
    if (!target.IsRealtimeConnected) await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/commands/{command.CommandId}", new { command.CommandId, command.ExpiresAt });
});

cloud.MapGet("/playback/commands", async (Guid deviceId, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var now = clock.GetUtcNow();
    var commandEntities = await db.PlaybackCommands.AsNoTracking()
        .Where(x => x.AccountId == accountId && x.TargetDeviceId == deviceId && x.AcknowledgedAt == null && x.ExpiresAt > now)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync(cancellationToken);
    // JsonDocument.RootElement is not translatable by Npgsql. Project after
    // materialization and clone the element so JSON serialization is not tied
    // to the tracked entity/document lifetime.
    var commands = commandEntities.Select(x => new PlaybackCommandResponse(
        x.CommandId, x.SourceDeviceId, x.TargetDeviceId, x.Type,
        x.Payload.RootElement.Clone(), x.ExpiresAt)).ToList();
    return Results.Ok(commands);
});

cloud.MapPost("/playback/commands/{commandId:guid}/ack", async (Guid commandId, PlaybackCommandAckRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, IHubContext<PlaybackHub> hub, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var command = await db.PlaybackCommands.SingleOrDefaultAsync(x => x.AccountId == accountId && x.CommandId == commandId && x.TargetDeviceId == request.TargetDeviceId, cancellationToken);
    if (command is null || command.ExpiresAt <= clock.GetUtcNow()) return Results.NotFound();
    command.AcknowledgedAt = clock.GetUtcNow();
    command.Applied = request.Applied;
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.Group(PlaybackHub.Group(accountId, command.SourceDeviceId)).SendAsync("playbackCommandAcknowledged", command.CommandId, request.Applied, cancellationToken);
    return Results.NoContent();
});

cloud.MapGet("/playback/session", async (HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session = await db.PlaybackSessions.AsNoTracking()
        .Where(x => x.AccountId == accountId && x.EndedAt == null)
        .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(cancellationToken);
    if (session is null) return Results.NoContent();
    return Results.Ok(new PlaybackSessionResponse(session.SessionId, session.TargetDeviceId,
        session.Sequence, session.State.RootElement.Clone(), session.UpdatedAt));
});

cloud.MapPost("/playback/session/start", async (PlaybackSessionStartRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, IHubContext<PlaybackHub> hub,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        request.SourceDeviceId == request.TargetDeviceId || !IsPortablePlaybackPayload(request.State))
        return Results.BadRequest(new { code = "invalid_playback_session" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var devices = await db.Devices.Where(x => x.AccountId == accountId &&
        (x.DeviceId == request.SourceDeviceId || x.DeviceId == request.TargetDeviceId)).ToListAsync(cancellationToken);
    if (devices.Count != 2) return Results.NotFound(new { code = "device_not_found" });
    var now = clock.GetUtcNow();
    var old = await db.PlaybackSessions.Where(x => x.AccountId == accountId && x.EndedAt == null).ToListAsync(cancellationToken);
    foreach (var previous in old) previous.EndedAt = now;
    var session = new PlaybackSessionEntity { AccountId = accountId, SessionId = Guid.NewGuid(),
        TargetDeviceId = request.TargetDeviceId, State = JsonDocument.Parse(request.State.GetRawText()),
        Sequence = 1, UpdatedAt = now };
    db.PlaybackSessions.Add(session);
    var command = new PlaybackCommandEntity { AccountId = accountId, CommandId = Guid.NewGuid(),
        SourceDeviceId = request.SourceDeviceId, TargetDeviceId = request.TargetDeviceId, Type = "handoff",
        Payload = JsonDocument.Parse(request.State.GetRawText()), CreatedAt = now, ExpiresAt = now.AddMinutes(1) };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.Group(PlaybackHub.Group(accountId, request.TargetDeviceId)).SendAsync("playbackCommandAvailable", command.CommandId, cancellationToken);
    var target = devices.Single(x => x.DeviceId == request.TargetDeviceId);
    if (!target.IsRealtimeConnected) await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/session/{session.SessionId}",
        new { sessionId = session.SessionId, commandId = command.CommandId });
});

cloud.MapPost("/playback/session/command", async (PlaybackSessionCommandRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, IHubContext<PlaybackHub> hub,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Type) || !IsPortablePlaybackPayload(request.Payload))
        return Results.BadRequest(new { code = "invalid_playback_command" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session = await db.PlaybackSessions.FirstOrDefaultAsync(x => x.AccountId == accountId && x.EndedAt == null, cancellationToken);
    if (session is null || session.TargetDeviceId != request.TargetDeviceId) return Results.Conflict(new { code = "session_not_active" });
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId, cancellationToken)) return Results.NotFound();
    var now = clock.GetUtcNow();
    session.Sequence++;
    session.UpdatedAt = now;
    var command = new PlaybackCommandEntity { AccountId = accountId, CommandId = Guid.NewGuid(), SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId, Type = request.Type, Payload = JsonDocument.Parse(request.Payload.GetRawText()), CreatedAt = now, ExpiresAt = now.AddMinutes(1) };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.Group(PlaybackHub.Group(accountId, request.TargetDeviceId)).SendAsync("playbackCommandAvailable", command.CommandId, cancellationToken);
    var target = await db.Devices.SingleAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId, cancellationToken);
    if (!target.IsRealtimeConnected) await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/commands/{command.CommandId}", new { commandId = command.CommandId, sequence = session.Sequence });
});

cloud.MapPost("/playback/session/state", async (PlaybackSessionStateRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.DeviceId == Guid.Empty || !IsPortablePlaybackPayload(request.State)) return Results.BadRequest();
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session = await db.PlaybackSessions.FirstOrDefaultAsync(x => x.AccountId == accountId && x.EndedAt == null && x.TargetDeviceId == request.DeviceId, cancellationToken);
    if (session is null) return Results.NotFound();
    session.Sequence++;
    session.State = JsonDocument.Parse(request.State.GetRawText());
    session.UpdatedAt = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { sequence = session.Sequence });
});

cloud.MapPost("/playback/session/target", async (PlaybackSessionTargetRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, IHubContext<PlaybackHub> hub,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty || !IsPortablePlaybackPayload(request.State))
        return Results.BadRequest(new { code = "invalid_playback_target" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session = await db.PlaybackSessions.FirstOrDefaultAsync(x => x.AccountId == accountId && x.EndedAt == null, cancellationToken);
    if (session is null) return Results.NotFound(new { code = "session_not_active" });
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId, cancellationToken) ||
        !await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId, cancellationToken))
        return Results.NotFound(new { code = "device_not_found" });
    var now = clock.GetUtcNow();
    session.TargetDeviceId = request.TargetDeviceId;
    session.Sequence++;
    session.State = JsonDocument.Parse(request.State.GetRawText());
    session.UpdatedAt = now;
    var command = new PlaybackCommandEntity { AccountId = accountId, CommandId = Guid.NewGuid(), SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId, Type = "handoff", Payload = JsonDocument.Parse(request.State.GetRawText()), CreatedAt = now, ExpiresAt = now.AddMinutes(1) };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.Group(PlaybackHub.Group(accountId, request.TargetDeviceId)).SendAsync("playbackCommandAvailable", command.CommandId, cancellationToken);
    var target = await db.Devices.SingleAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId, cancellationToken);
    if (!target.IsRealtimeConnected) await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted((string?)null, new { commandId = command.CommandId, sequence = session.Sequence });
});

cloud.MapDelete("/playback/session", async (HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var sessions = await db.PlaybackSessions.Where(x => x.AccountId == accountId && x.EndedAt == null).ToListAsync(cancellationToken);
    foreach (var session in sessions) session.EndedAt = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

cloud.MapPost("/sync", async (
    SyncRequest request, HttpContext context, AccountIdentity identity,
    SyncService sync, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await sync.SyncAsync(identity.Resolve(context), request, cancellationToken));
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { code = exception.Message });
    }
});

cloud.MapPost("/sync/pause", async (
    PauseSyncRequest request, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var updated = await db.Devices
        .Where(x => x.AccountId == accountId && x.DeviceId == request.DeviceId)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.SyncPaused, request.Paused)
            .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), cancellationToken);
    return updated == 1 ? Results.NoContent() : Results.NotFound();
});

cloud.MapPost("/audio/next", async (
    AudioNextRequest request,
    ResolverBackupClient resolver,
    CancellationToken cancellationToken) =>
{
    if (request.DeviceId == Guid.Empty || request.VideoIds.Count is < 1 or > 500)
        return Results.BadRequest(new { code = "invalid_audio_inventory" });
    return Results.Ok(await resolver.NextAsync(request.VideoIds, cancellationToken));
});

cloud.MapDelete("/account", async (
    HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    await db.Snapshots.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    await db.SyncEvents.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    await db.Devices.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    await db.PlaybackCommands.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    await db.PlaybackSessions.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Results.NoContent();
});

app.MapHub<PlaybackHub>("/cloud/v1/playback/hub");

app.Run();

static bool IsPortablePlaybackPayload(JsonElement payload)
{
    // Queue metadata can legitimately contain hundreds of songs. It remains
    // sanitized and bounded, but must not be confused with audio/file data.
    if (payload.GetRawText().Length > 1024 * 1024) return false;
    return !ContainsForbiddenKey(payload);
}

static bool ContainsForbiddenKey(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.Object => value.EnumerateObject().Any(property =>
        property.Name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
        property.Name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        ContainsForbiddenKey(property.Value)),
    JsonValueKind.Array => value.EnumerateArray().Any(ContainsForbiddenKey),
    _ => false
};

public partial class Program;
