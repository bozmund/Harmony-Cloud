using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Harmony.Cloud.Api.Playback;

/// <summary>
/// Tracks live playback sockets per account so the API can push to one device or fan out to the
/// rest. Replaces the SignalR hub's group bookkeeping. In-process only: a device's frames go to the
/// replica holding its socket, which is why the durable session row stays the source of truth and
/// every client re-reads a snapshot on connect.
/// </summary>
public sealed class PlaybackConnectionRegistry(ILogger<PlaybackConnectionRegistry> logger)
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, PlaybackConnection>> _accounts = new(StringComparer.Ordinal);

    public PlaybackConnection Add(string accountId, Guid deviceId, WebSocket socket)
    {
        var connection = new PlaybackConnection(socket);
        var devices = _accounts.GetOrAdd(accountId, _ => new ConcurrentDictionary<Guid, PlaybackConnection>());
        // A device reconnecting before the old socket is reaped must not leave a stale entry that
        // shadows the live one.
        if (devices.TryRemove(deviceId, out var previous)) previous.Abort();
        devices[deviceId] = connection;
        return connection;
    }

    public void Remove(string accountId, Guid deviceId, PlaybackConnection connection)
    {
        if (!_accounts.TryGetValue(accountId, out var devices)) return;
        // Only remove our own entry: a newer socket for the same device may already have replaced it.
        if (devices.TryGetValue(deviceId, out var current) && ReferenceEquals(current, connection))
            devices.TryRemove(deviceId, out _);
        if (devices.IsEmpty) _accounts.TryRemove(accountId, out _);
    }

    public bool IsConnected(string accountId, Guid deviceId) =>
        _accounts.TryGetValue(accountId, out var devices)
        && devices.TryGetValue(deviceId, out var connection)
        && connection.IsOpen;

    public Task SendAsync<TFrame>(string accountId, Guid deviceId, TFrame frame, CancellationToken cancellationToken)
    {
        if (!_accounts.TryGetValue(accountId, out var devices)
            || !devices.TryGetValue(deviceId, out var connection))
            return Task.CompletedTask;
        return SendSafeAsync(connection, frame, cancellationToken);
    }

    public async Task BroadcastAsync<TFrame>(
        string accountId, TFrame frame, Guid? exceptDeviceId, CancellationToken cancellationToken)
    {
        if (!_accounts.TryGetValue(accountId, out var devices)) return;
        var sends = devices
            .Where(entry => exceptDeviceId is null || entry.Key != exceptDeviceId)
            .Select(entry => SendSafeAsync(entry.Value, frame, cancellationToken));
        await Task.WhenAll(sends);
    }

    private async Task SendSafeAsync<TFrame>(
        PlaybackConnection connection, TFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A dead socket is normal — the reader loop will notice and clean up. Never let one
            // broken client fail a broadcast to the others.
            logger.LogDebug(exception, "Dropping playback frame for a closed socket.");
            connection.Abort();
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// One device's socket. Sends are serialized through a semaphore because a WebSocket permits only
/// one outstanding send, and progress broadcasts race command pushes.
/// </summary>
public sealed class PlaybackConnection(WebSocket socket) : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsOpen => socket.State == WebSocketState.Open;

    public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (!IsOpen) return;
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsOpen) return;
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Abort()
    {
        try { socket.Abort(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => _sendLock.Dispose();
}
