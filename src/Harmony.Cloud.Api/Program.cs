using Harmony.Cloud.Api.Audio;
using Harmony.Cloud.Api.Abstractions;
using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Domain;
using Harmony.Cloud.Api.Diagnostics;
using Harmony.Cloud.Api.Endpoints;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Playback;
using Harmony.Cloud.Api.Security;
using Harmony.Cloud.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("Cloud").Get<CloudOptions>()
              ?? throw new InvalidOperationException("Cloud configuration is required.");
if (options.IdentityHmacKey.Length < 32)
    throw new InvalidOperationException("Cloud:IdentityHmacKey must contain at least 32 characters.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountIdentity>();
builder.Services.AddSingleton<FcmWakeupService>();
builder.Services.AddSingleton<CloudMetrics>();
builder.Services.AddSingleton<ReadinessProbe>();
builder.Services.AddScoped<ISyncService, SyncService>();
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

builder.Services.AddSingleton<PlaybackConnectionRegistry>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("harmony-cloud-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        var endpoint = builder.Configuration["OTLP:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
    })
    .WithMetrics(metrics => metrics
        .AddMeter(CloudMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CloudDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await CloudSchemaMigrator.MigrateAsync(db);
    return;
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    await Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Cloud failure",
        extensions: new Dictionary<string, object?> { ["traceId"] = traceId }).ExecuteAsync(context);
}));
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost |
                       ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.UseWebSockets();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Harmony Cloud API v1");
        options.DocumentTitle = "Harmony Cloud API";
    });
}

// How long an unacknowledged playback command stays deliverable. One minute was sized for the old
// one-second poll; delivery is now push-only, so a target that is asleep or reconnecting needs a
// window wide enough to come back and drain it.
var PlaybackCommandLifetime = TimeSpan.FromMinutes(5);

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/cloud/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (ReadinessProbe probe, CancellationToken cancellationToken) =>
    await probe.IsReadyAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapPrometheusScrapingEndpoint("/metrics");
var cloud = app.MapGroup("/cloud/v1");
if (authEnabled) cloud.RequireAuthorization();
cloud.MapSyncEndpoints();
cloud.MapAudioEndpoints();
cloud.MapAccountEndpoints();
cloud.MapDeviceEndpoints();

cloud.MapGet("/playback/devices", async (HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets, Guid currentDeviceId,
    CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var targetDeviceId = await db.PlaybackSessions.AsNoTracking()
        .Where(x => x.AccountId == accountId && x.EndedAt == null)
        .OrderByDescending(x => x.UpdatedAt).Select(x => (Guid?)x.TargetDeviceId)
        .FirstOrDefaultAsync(cancellationToken);
    var devices = await db.Devices.AsNoTracking().Where(x => x.AccountId == accountId)
        .OrderBy(x => x.Name).ToListAsync(cancellationToken);
    // Presence is projected after materialization because it now depends on the live socket table.
    // "online" must mean a socket we can actually push to: with no polling fallback, a device whose
    // socket has dropped cannot be controlled, however recently it was last seen. A device with no
    // socket but a push token is "background" (FCM can wake it); anything else — notably a closed
    // Windows app, which never registers for FCM — is honestly "unavailable".
    var response = devices.Select(x => new PlaybackDeviceResponse(
        x.DeviceId, x.Name, x.Platform, x.AppVersion,
        sockets.IsConnected(accountId, x.DeviceId) ? "online"
            : x.PushTokenCiphertext != null ? "background" : "unavailable",
        x.DeviceId == currentDeviceId, targetDeviceId == x.DeviceId)).ToList();
    return Results.Ok(response);
});

cloud.MapPost("/playback/commands", async (PlaybackCommandRequest request, HttpContext context,
    AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets, FcmWakeupService fcm, TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        request.SourceDeviceId == request.TargetDeviceId ||
        string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 48 ||
        !PlaybackPayload.IsPortable(request.Payload))
        return Results.BadRequest(new { code = "invalid_playback_command" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId,
            cancellationToken))
        return Results.NotFound();
    var target =
        await db.Devices.SingleOrDefaultAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId,
            cancellationToken);
    if (target is null) return Results.NotFound();
    var now = clock.GetUtcNow();
    var command = new PlaybackCommandEntity
    {
        AccountId = accountId,
        CommandId = Guid.NewGuid(),
        SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId,
        Type = request.Type,
        Payload = JsonDocument.Parse(request.Payload.GetRawText()),
        CreatedAt = now,
        ExpiresAt = now + PlaybackCommandLifetime
    };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    // The payload rides inline, so the target acts on receipt instead of calling back for it.
    await sockets.SendAsync(accountId, request.TargetDeviceId, new CommandFrame(
        command.CommandId, command.SourceDeviceId, command.TargetDeviceId, command.Type,
        command.Payload.RootElement.Clone(), command.ExpiresAt), cancellationToken);
    // FCM is now purely a "wake up and open your socket" nudge for a backgrounded device.
    if (!sockets.IsConnected(accountId, request.TargetDeviceId))
        await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/commands/{command.CommandId}",
        new { command.CommandId, command.ExpiresAt });
});

cloud.MapGet("/playback/commands", async (Guid deviceId, HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var now = clock.GetUtcNow();
    var commandEntities = await db.PlaybackCommands.AsNoTracking()
        .Where(x => x.AccountId == accountId && x.TargetDeviceId == deviceId && x.AcknowledgedAt == null &&
                    x.ExpiresAt > now)
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

cloud.MapPost("/playback/commands/{commandId:guid}/ack", async (Guid commandId, PlaybackCommandAckRequest request,
    HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets, TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var command = await db.PlaybackCommands.SingleOrDefaultAsync(
        x => x.AccountId == accountId && x.CommandId == commandId && x.TargetDeviceId == request.TargetDeviceId,
        cancellationToken);
    if (command is null || command.ExpiresAt <= clock.GetUtcNow()) return Results.NotFound();
    command.AcknowledgedAt = clock.GetUtcNow();
    command.Applied = request.Applied;
    await db.SaveChangesAsync(cancellationToken);
    await sockets.SendAsync(accountId, command.SourceDeviceId,
        new { type = "commandAcknowledged", commandId = command.CommandId, applied = request.Applied },
        cancellationToken);
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
        session.Sequence, session.State.RootElement.Clone(), session.UpdatedAt,
        session.CurrentSongId, session.PositionMs, session.DurationMs, session.Playing,
        session.ProgressUpdatedAt));
});

cloud.MapPost("/playback/session/start", async (PlaybackSessionStartRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        request.SourceDeviceId == request.TargetDeviceId || !PlaybackPayload.IsPortable(request.State))
        return Results.BadRequest(new { code = "invalid_playback_session" });
    if (!PlaybackSessionState.IsValid(request.State, out var stateFailure))
        return Results.BadRequest(new { code = stateFailure });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var devices = await db.Devices.Where(x => x.AccountId == accountId &&
                                              (x.DeviceId == request.SourceDeviceId ||
                                               x.DeviceId == request.TargetDeviceId)).ToListAsync(cancellationToken);
    if (devices.Count != 2) return Results.NotFound(new { code = "device_not_found" });
    var now = clock.GetUtcNow();
    var old = await db.PlaybackSessions.Where(x => x.AccountId == accountId && x.EndedAt == null)
        .ToListAsync(cancellationToken);
    foreach (var previous in old) previous.EndedAt = now;
    var session = new PlaybackSessionEntity
    {
        AccountId = accountId,
        SessionId = Guid.NewGuid(),
        TargetDeviceId = request.TargetDeviceId,
        State = JsonDocument.Parse(request.State.GetRawText()),
        Sequence = 1,
        UpdatedAt = now
    };
    db.PlaybackSessions.Add(session);
    var command = new PlaybackCommandEntity
    {
        AccountId = accountId,
        CommandId = Guid.NewGuid(),
        SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId,
        Type = "handoff",
        Payload = JsonDocument.Parse(request.State.GetRawText()),
        CreatedAt = now,
        ExpiresAt = now + PlaybackCommandLifetime
    };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    // The payload rides inline, so the target acts on receipt instead of calling back for it.
    await sockets.SendAsync(accountId, request.TargetDeviceId, new CommandFrame(
        command.CommandId, command.SourceDeviceId, command.TargetDeviceId, command.Type,
        command.Payload.RootElement.Clone(), command.ExpiresAt), cancellationToken);
    // Everyone else needs to know a session now exists and who owns the audio.
    await sockets.BroadcastAsync(accountId, SessionSnapshotFrame.From(session),
        exceptDeviceId: request.TargetDeviceId, cancellationToken);
    var target = devices.Single(x => x.DeviceId == request.TargetDeviceId);
    // FCM is now purely a "wake up and open your socket" nudge for a backgrounded device.
    if (!sockets.IsConnected(accountId, request.TargetDeviceId))
        await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/session/{session.SessionId}",
        new { sessionId = session.SessionId, commandId = command.CommandId });
});

cloud.MapPost("/playback/session/command", async (PlaybackSessionCommandRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Type) || !PlaybackPayload.IsPortable(request.Payload))
        return Results.BadRequest(new { code = "invalid_playback_command" });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session =
        await db.PlaybackSessions.FirstOrDefaultAsync(x => x.AccountId == accountId && x.EndedAt == null,
            cancellationToken);
    if (session is null || session.TargetDeviceId != request.TargetDeviceId)
        return Results.Conflict(new { code = "session_not_active" });
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId,
            cancellationToken)) return Results.NotFound();
    var now = clock.GetUtcNow();
    session.Sequence++;
    session.UpdatedAt = now;
    var command = new PlaybackCommandEntity
    {
        AccountId = accountId,
        CommandId = Guid.NewGuid(),
        SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId,
        Type = request.Type,
        Payload = JsonDocument.Parse(request.Payload.GetRawText()),
        CreatedAt = now,
        ExpiresAt = now + PlaybackCommandLifetime
    };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    // The payload rides inline, so the target acts on receipt instead of calling back for it.
    await sockets.SendAsync(accountId, request.TargetDeviceId, new CommandFrame(
        command.CommandId, command.SourceDeviceId, command.TargetDeviceId, command.Type,
        command.Payload.RootElement.Clone(), command.ExpiresAt), cancellationToken);
    var target = await db.Devices.SingleAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId,
        cancellationToken);
    // FCM is now purely a "wake up and open your socket" nudge for a backgrounded device.
    if (!sockets.IsConnected(accountId, request.TargetDeviceId))
        await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted($"/cloud/v1/playback/commands/{command.CommandId}",
        new { commandId = command.CommandId, sequence = session.Sequence });
});

// Updates the DURABLE state — the queue and index. This used to be called once per second with a
// full state blob, rewriting the jsonb column and bumping the sequence every tick; live progress now
// travels over the socket instead, so this fires only when the queue actually changes.
cloud.MapPost("/playback/session/state", async (PlaybackSessionStateRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets,
    TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.DeviceId == Guid.Empty || !PlaybackPayload.IsPortable(request.State))
        return Results.BadRequest(new { code = "invalid_session_state" });
    if (!PlaybackSessionState.IsValid(request.State, out var failureCode))
        return Results.BadRequest(new { code = failureCode });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session = await db.PlaybackSessions.FirstOrDefaultAsync(
        x => x.AccountId == accountId && x.EndedAt == null && x.TargetDeviceId == request.DeviceId, cancellationToken);
    if (session is null) return Results.NotFound();
    // Ignore a stale writer: a retry or an out-of-order publish must not roll the queue backwards.
    if (PlaybackSessionState.QueueRevision(request.State)
        < PlaybackSessionState.QueueRevision(session.State.RootElement))
        return Results.Ok(new { sequence = session.Sequence, applied = false });

    session.Sequence++;
    session.State = JsonDocument.Parse(request.State.GetRawText());
    session.UpdatedAt = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    await sockets.BroadcastAsync(accountId, SessionSnapshotFrame.From(session),
        exceptDeviceId: request.DeviceId, cancellationToken);
    return Results.Ok(new { sequence = session.Sequence, applied = true });
});

cloud.MapPost("/playback/session/target", async (PlaybackSessionTargetRequest request, HttpContext context,
    AccountIdentity identity, IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets,
    FcmWakeupService fcm, TimeProvider clock, CancellationToken cancellationToken) =>
{
    if (request.SourceDeviceId == Guid.Empty || request.TargetDeviceId == Guid.Empty ||
        !PlaybackPayload.IsPortable(request.State))
        return Results.BadRequest(new { code = "invalid_playback_target" });
    if (!PlaybackSessionState.IsValid(request.State, out var targetStateFailure))
        return Results.BadRequest(new { code = targetStateFailure });
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var session =
        await db.PlaybackSessions.FirstOrDefaultAsync(x => x.AccountId == accountId && x.EndedAt == null,
            cancellationToken);
    if (session is null) return Results.NotFound(new { code = "session_not_active" });
    if (!await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.SourceDeviceId,
            cancellationToken) ||
        !await db.Devices.AnyAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId,
            cancellationToken))
        return Results.NotFound(new { code = "device_not_found" });
    var now = clock.GetUtcNow();
    session.TargetDeviceId = request.TargetDeviceId;
    session.Sequence++;
    session.State = JsonDocument.Parse(request.State.GetRawText());
    session.UpdatedAt = now;
    // Persisted progress belonged to the previous target. Clear it so a device reading the snapshot
    // during the handoff does not show the old device's position against the new target.
    session.Playing = false;
    session.ProgressUpdatedAt = null;
    var command = new PlaybackCommandEntity
    {
        AccountId = accountId,
        CommandId = Guid.NewGuid(),
        SourceDeviceId = request.SourceDeviceId,
        TargetDeviceId = request.TargetDeviceId,
        Type = "handoff",
        Payload = JsonDocument.Parse(request.State.GetRawText()),
        CreatedAt = now,
        ExpiresAt = now + PlaybackCommandLifetime
    };
    db.PlaybackCommands.Add(command);
    await db.SaveChangesAsync(cancellationToken);
    // The payload rides inline, so the target acts on receipt instead of calling back for it.
    await sockets.SendAsync(accountId, request.TargetDeviceId, new CommandFrame(
        command.CommandId, command.SourceDeviceId, command.TargetDeviceId, command.Type,
        command.Payload.RootElement.Clone(), command.ExpiresAt), cancellationToken);
    // Everyone else must learn that the audio target moved, including the device that just lost it.
    await sockets.BroadcastAsync(accountId, SessionSnapshotFrame.From(session),
        exceptDeviceId: request.TargetDeviceId, cancellationToken);
    var target = await db.Devices.SingleAsync(x => x.AccountId == accountId && x.DeviceId == request.TargetDeviceId,
        cancellationToken);
    // FCM is now purely a "wake up and open your socket" nudge for a backgrounded device.
    if (!sockets.IsConnected(accountId, request.TargetDeviceId))
        await fcm.WakeAsync(target.PushTokenCiphertext, command.CommandId, cancellationToken);
    return Results.Accepted((string?)null, new { commandId = command.CommandId, sequence = session.Sequence });
});

cloud.MapDelete("/playback/session", async (HttpContext context, AccountIdentity identity,
    IDbContextFactory<CloudDbContext> contexts, PlaybackConnectionRegistry sockets, TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    var accountId = identity.Resolve(context);
    await using var db = await contexts.CreateDbContextAsync(cancellationToken);
    var sessions = await db.PlaybackSessions.Where(x => x.AccountId == accountId && x.EndedAt == null)
        .ToListAsync(cancellationToken);
    foreach (var session in sessions) session.EndedAt = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    // Without a poll to notice the session vanished, every device has to be told.
    foreach (var session in sessions)
        await sockets.BroadcastAsync(accountId, new SessionEndedFrame(session.SessionId),
            exceptDeviceId: null, cancellationToken);
    return Results.NoContent();
});

// Mapped on the `cloud` group so it inherits RequireAuthorization(). The old MapHub was registered
// on `app` and did not, relying solely on the hub class's [Authorize].
cloud.MapGet("/playback/socket", PlaybackSocketEndpoint.HandleAsync).ExcludeFromDescription();

app.Run();