using System.Net.WebSockets;
using System.Text.Json;
using Harmony.Cloud.Api.Persistence;
using Harmony.Cloud.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Playback;

/// <summary>
/// The single realtime channel for playback. Replaces SignalR: ASP.NET Core's built-in WebSocket
/// support needs no third-party client, and authentication rides on the standard
/// <c>Authorization</c> header of the upgrade request rather than a token in the query string.
/// </summary>
public static class PlaybackSocketEndpoint
{
    /// How often a live target's progress is written to Postgres. Progress itself is broadcast on
    /// every tick; persistence exists only so a device joining late sees a sane position, so a
    /// per-second row rewrite would be pure waste.
    public static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromSeconds(10);

    public static async Task HandleAsync(
        HttpContext context,
        AccountIdentity identity,
        PlaybackConnectionRegistry registry,
        IDbContextFactory<CloudDbContext> contexts,
        TimeProvider clock,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Harmony.Cloud.Api.Playback.Socket");
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!Guid.TryParse(context.Request.Query["deviceId"], out var deviceId) || deviceId == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string accountId;
        try
        {
            accountId = identity.Resolve(context);
        }
        catch (UnauthorizedAccessException)
        {
            // An unauthenticated upgrade is a 401, not the 500 the generic exception handler would
            // produce — the client needs to tell "refresh your token" from "the server is broken".
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await using (var db = await contexts.CreateDbContextAsync(context.RequestAborted))
        {
            var device = await db.Devices.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.DeviceId == deviceId, context.RequestAborted);
            if (device is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            device.IsRealtimeConnected = true;
            device.LastSeenAt = clock.GetUtcNow();
            await db.SaveChangesAsync(context.RequestAborted);
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = registry.Add(accountId, deviceId, socket);
        try
        {
            await SendSnapshotAsync(registry, contexts, accountId, deviceId, context.RequestAborted);
            await ReadLoopAsync(socket, registry, contexts, clock, accountId, deviceId, logger, context.RequestAborted);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            registry.Remove(accountId, deviceId, connection);
            connection.Dispose();
            await MarkDisconnectedAsync(registry, contexts, clock, accountId, deviceId);
        }
    }

    private static async Task SendSnapshotAsync(
        PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts,
        string accountId, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var session = await db.PlaybackSessions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.EndedAt == null)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (session is null) return;
        await registry.SendAsync(accountId, deviceId, new SessionSnapshotFrame(
            session.SessionId, session.TargetDeviceId, session.Sequence,
            session.State.RootElement.Clone(), session.UpdatedAt), cancellationToken);
    }

    private static async Task ReadLoopAsync(
        WebSocket socket, PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts,
        TimeProvider clock, string accountId, Guid deviceId, ILogger logger, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        var lastPersistedAt = DateTimeOffset.MinValue;
        string? lastPersistedSongId = null;
        bool? lastPersistedPlaying = null;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return;
            }
            if (received.MessageType != WebSocketMessageType.Text || received.Count == 0) continue;
            if (!received.EndOfMessage)
            {
                // Frames are small by construction; anything larger is a protocol violation.
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame_too_large", CancellationToken.None);
                return;
            }

            ClientFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<ClientFrame>(
                    buffer.AsSpan(0, received.Count), PlaybackConnectionRegistry.JsonOptions);
            }
            catch (JsonException)
            {
                logger.LogDebug("Discarding malformed playback frame from {DeviceId}.", deviceId);
                continue;
            }
            if (frame is null) continue;

            switch (frame.Type)
            {
                case PlaybackFrameTypes.Ping:
                    continue;
                case PlaybackFrameTypes.Ack when frame.CommandId is { } commandId:
                    await AcknowledgeAsync(registry, contexts, clock, accountId, deviceId, commandId,
                        frame.Applied ?? false, cancellationToken);
                    continue;
                case PlaybackFrameTypes.ClientProgress:
                    await HandleProgressAsync(registry, contexts, clock, accountId, deviceId, frame,
                        ref lastPersistedAt, ref lastPersistedSongId, ref lastPersistedPlaying, cancellationToken);
                    continue;
            }
        }
    }

    private static Task HandleProgressAsync(
        PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts, TimeProvider clock,
        string accountId, Guid deviceId, ClientFrame frame,
        ref DateTimeOffset lastPersistedAt, ref string? lastPersistedSongId, ref bool? lastPersistedPlaying,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // Persist on anything a late joiner could not infer — a new song, a play/pause edge — and
        // otherwise no more than once per interval.
        var persist = frame.CurrentSongId != lastPersistedSongId
            || frame.Playing != lastPersistedPlaying
            || now - lastPersistedAt >= ProgressPersistInterval;
        if (persist)
        {
            lastPersistedAt = now;
            lastPersistedSongId = frame.CurrentSongId;
            lastPersistedPlaying = frame.Playing;
        }
        return PublishProgressAsync(registry, contexts, accountId, deviceId, frame, now, persist, cancellationToken);
    }

    private static async Task PublishProgressAsync(
        PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts,
        string accountId, Guid deviceId, ClientFrame frame, DateTimeOffset now, bool persist,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var session = await db.PlaybackSessions.FirstOrDefaultAsync(
            x => x.AccountId == accountId && x.EndedAt == null && x.TargetDeviceId == deviceId,
            cancellationToken);
        // Only the audio target may publish progress; anything else is a stale controller.
        if (session is null) return;

        if (persist)
        {
            session.PositionMs = frame.PositionMs ?? 0;
            session.DurationMs = frame.DurationMs;
            session.Playing = frame.Playing ?? false;
            session.CurrentSongId = frame.CurrentSongId;
            session.ProgressUpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        await registry.BroadcastAsync(accountId, new ProgressFrame(
            session.SessionId, deviceId, frame.CurrentSongId, frame.PositionMs ?? 0, frame.DurationMs,
            frame.Playing ?? false, frame.Speed ?? 1.0,
            frame.PublishedAtMs ?? now.ToUnixTimeMilliseconds()), exceptDeviceId: deviceId, cancellationToken);
    }

    private static async Task AcknowledgeAsync(
        PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts, TimeProvider clock,
        string accountId, Guid deviceId, Guid commandId, bool applied, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var command = await db.PlaybackCommands.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.CommandId == commandId && x.TargetDeviceId == deviceId,
            cancellationToken);
        if (command is null) return;
        command.AcknowledgedAt = clock.GetUtcNow();
        command.Applied = applied;
        await db.SaveChangesAsync(cancellationToken);
        await registry.SendAsync(accountId, command.SourceDeviceId,
            new { type = "commandAcknowledged", commandId, applied }, cancellationToken);
    }

    private static async Task MarkDisconnectedAsync(
        PlaybackConnectionRegistry registry, IDbContextFactory<CloudDbContext> contexts,
        TimeProvider clock, string accountId, Guid deviceId)
    {
        // A reconnect may have replaced this socket before its read loop exits. The registry owns
        // live presence, so only the last connection for a device may persist an offline state.
        if (registry.IsConnected(accountId, deviceId)) return;
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            var device = await db.Devices.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.DeviceId == deviceId);
            if (device is null) return;
            device.IsRealtimeConnected = false;
            device.LastSeenAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
        }
        catch (Exception)
        {
            // Shutdown races must not surface as request failures; presence self-heals via LastSeenAt.
        }
    }
}
