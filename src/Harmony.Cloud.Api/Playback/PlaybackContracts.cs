using System.Text.Json;

namespace Harmony.Cloud.Api.Playback;

public sealed record DeviceRegistrationRequest(Guid DeviceId, string Name, string Platform, string AppVersion);
public sealed record DevicePresenceRequest(Guid DeviceId);
public sealed record PushRegistrationRequest(Guid DeviceId, string Provider, string Token);
public sealed record PlaybackCommandRequest(Guid SourceDeviceId, Guid TargetDeviceId, string Type, JsonElement Payload);
public sealed record PlaybackCommandAckRequest(Guid TargetDeviceId, bool Applied);
public sealed record PlaybackDeviceResponse(Guid DeviceId, string Name, string Platform, string AppVersion, string Presence, bool IsCurrentDevice, bool IsAudioTarget);
public sealed record PlaybackCommandResponse(Guid CommandId, Guid SourceDeviceId, Guid TargetDeviceId, string Type, JsonElement Payload, DateTimeOffset ExpiresAt);
public sealed record PlaybackSessionStartRequest(Guid SourceDeviceId, Guid TargetDeviceId, JsonElement State);
public sealed record PlaybackSessionCommandRequest(Guid SourceDeviceId, Guid TargetDeviceId, string Type, JsonElement Payload);
public sealed record PlaybackSessionStateRequest(Guid DeviceId, JsonElement State);
public sealed record PlaybackSessionTargetRequest(Guid SourceDeviceId, Guid TargetDeviceId, JsonElement State);
/// <summary>
/// A device declaring itself the audio target for what it is already playing, so other
/// devices can subscribe to it as a remote.
/// </summary>
/// <remarks>
/// Distinct from <see cref="PlaybackSessionStartRequest"/>, which is a handoff: one device
/// pushing its queue onto another. Here there is no second device and nothing is handed
/// anywhere — no command is emitted, and the caller keeps playing exactly as it was.
/// </remarks>
public sealed record PlaybackSessionClaimRequest(Guid DeviceId, JsonElement State);
/// <param name="State">Durable v2 state: ordered queueIds, index, and modes.</param>
/// <param name="PositionMs">
/// Last persisted progress. Only periodically written (live progress arrives over the socket), so a
/// device that joins mid-session opens on a sensible position rather than zero.
/// </param>
public sealed record PlaybackSessionResponse(
    Guid SessionId,
    Guid TargetDeviceId,
    long Sequence,
    JsonElement State,
    DateTimeOffset UpdatedAt,
    string? CurrentSongId = null,
    long PositionMs = 0,
    long? DurationMs = null,
    bool Playing = false,
    DateTimeOffset? ProgressUpdatedAt = null);
