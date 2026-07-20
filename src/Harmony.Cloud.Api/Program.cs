using Harmony.Cloud.Api.Audio;
using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Security;
using Harmony.Cloud.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("Cloud").Get<CloudOptions>()
    ?? throw new InvalidOperationException("Cloud configuration is required.");
if (options.IdentityHmacKey.Length < 32)
    throw new InvalidOperationException("Cloud:IdentityHmacKey must contain at least 32 characters.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountIdentity>();
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

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CloudDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    // Initial deployment creates the isolated Harmony Cloud database from this model. Subsequent
    // schema changes must add explicit EF migrations before production rollout.
    await db.Database.EnsureCreatedAsync();
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
    device.UpdatedAt = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { request.DeviceId });
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
    await transaction.CommitAsync(cancellationToken);
    return Results.NoContent();
});

app.Run();

public partial class Program;
