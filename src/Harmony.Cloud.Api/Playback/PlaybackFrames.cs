using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harmony.Cloud.Api.Playback;

/// <summary>
/// Wire frames for <c>/cloud/v1/playback/socket</c>. Every frame is a JSON object with a
/// <c>type</c> discriminator. Commands carry their payload inline, which removes the
/// notify-then-fetch round trip the old SignalR hub required.
/// </summary>
public static class PlaybackFrameTypes
{
    // Server to client.
    public const string SessionSnapshot = "sessionSnapshot";
    public const string Progress = "progress";
    public const string Command = "command";
    public const string SessionEnded = "sessionEnded";

    // Client to server.
    public const string ClientProgress = "progress";
    public const string Ack = "ack";
    public const string Ping = "ping";
}

/// <param name="PositionMs">
/// Last persisted progress. Carried on the snapshot because a device joining mid-session must be
/// able to start at the right position and, crucially, know whether it should be playing — without
/// it the only <c>playing</c> signal is whatever happened to be embedded in the state document.
/// </param>
public sealed record SessionSnapshotFrame(
    Guid SessionId,
    Guid TargetDeviceId,
    long Sequence,
    JsonElement State,
    DateTimeOffset UpdatedAt,
    string? CurrentSongId = null,
    long PositionMs = 0,
    long? DurationMs = null,
    bool Playing = false)
{
    [JsonPropertyOrder(-1)]
    public string Type => PlaybackFrameTypes.SessionSnapshot;

    public static SessionSnapshotFrame From(Persistence.PlaybackSessionEntity session) => new(
        session.SessionId, session.TargetDeviceId, session.Sequence,
        session.State.RootElement.Clone(), session.UpdatedAt,
        session.CurrentSongId, session.PositionMs, session.DurationMs, session.Playing);
}

/// <param name="PublishedAtMs">
/// Unix milliseconds at which the target sampled <paramref name="PositionMs"/>. Controllers
/// extrapolate from this anchor, so it must travel with every progress frame.
/// </param>
public sealed record ProgressFrame(
    Guid SessionId,
    Guid TargetDeviceId,
    string? CurrentSongId,
    long PositionMs,
    long? DurationMs,
    bool Playing,
    double Speed,
    long PublishedAtMs)
{
    [JsonPropertyOrder(-1)]
    public string Type => PlaybackFrameTypes.Progress;
}

public sealed record CommandFrame(
    Guid CommandId,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    string CommandType,
    JsonElement Payload,
    DateTimeOffset ExpiresAt)
{
    [JsonPropertyOrder(-1)]
    public string Type => PlaybackFrameTypes.Command;
}

public sealed record SessionEndedFrame(Guid SessionId)
{
    [JsonPropertyOrder(-1)]
    public string Type => PlaybackFrameTypes.SessionEnded;
}

/// Client to server. <c>type</c> selects which of the optional members are meaningful.
public sealed record ClientFrame(
    string Type,
    Guid? CommandId = null,
    bool? Applied = null,
    string? CurrentSongId = null,
    long? PositionMs = null,
    long? DurationMs = null,
    bool? Playing = null,
    double? Speed = null,
    long? PublishedAtMs = null);
