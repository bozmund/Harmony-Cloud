using System.Text.Json;

namespace Harmony.Cloud.Api.Playback;

public sealed record DeviceRegistrationRequest(Guid DeviceId, string Name, string Platform, string AppVersion);
public sealed record DevicePresenceRequest(Guid DeviceId);
public sealed record PushRegistrationRequest(Guid DeviceId, string Provider, string Token);
public sealed record PlaybackCommandRequest(Guid SourceDeviceId, Guid TargetDeviceId, string Type, JsonElement Payload);
public sealed record PlaybackCommandAckRequest(Guid TargetDeviceId, bool Applied);
public sealed record PlaybackDeviceResponse(Guid DeviceId, string Name, string Platform, string AppVersion, string Presence, bool IsCurrentDevice);
public sealed record PlaybackCommandResponse(Guid CommandId, Guid SourceDeviceId, Guid TargetDeviceId, string Type, JsonElement Payload, DateTimeOffset ExpiresAt);
