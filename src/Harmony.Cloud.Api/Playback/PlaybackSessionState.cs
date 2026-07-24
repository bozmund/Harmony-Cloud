using System.Text.Json;

namespace Harmony.Cloud.Api.Playback;

/// <summary>
/// Validation for the durable session-state document.
///
/// v2 carries ordered <c>queueIds</c> only — never per-song metadata. Ids are ~12 bytes each, so a
/// 900-song queue is about 12 KB, and each device resolves its own display metadata. This also
/// keeps the document clear of <see cref="PlaybackPayload"/>'s <c>url</c>/<c>path</c> key deny-list,
/// which would otherwise reject the whole payload the moment artwork fields crept back in.
/// </summary>
public static class PlaybackSessionState
{
    public const int CurrentSchemaVersion = 2;

    /// Hard ceiling on queue length, so one client cannot pin an unbounded document in every row.
    public const int MaximumQueueLength = 5000;

    public static bool IsValid(JsonElement state, out string? failureCode)
    {
        failureCode = null;
        if (state.ValueKind != JsonValueKind.Object)
        {
            failureCode = "invalid_session_state";
            return false;
        }
        if (!state.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() != CurrentSchemaVersion)
        {
            failureCode = "unsupported_schema_version";
            return false;
        }
        if (!state.TryGetProperty("queueIds", out var queueIds) || queueIds.ValueKind != JsonValueKind.Array)
        {
            failureCode = "invalid_session_state";
            return false;
        }
        var length = queueIds.GetArrayLength();
        if (length is 0 or > MaximumQueueLength)
        {
            failureCode = "invalid_queue_length";
            return false;
        }
        foreach (var id in queueIds.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
            {
                failureCode = "invalid_session_state";
                return false;
            }
        }
        if (!state.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number
            || index.GetInt32() < 0 || index.GetInt32() >= length)
        {
            failureCode = "invalid_index";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Monotonic revision of the queue itself. Lets a device tell "the queue changed" from "only the
    /// position moved", so it can skip re-resolving metadata it already holds.
    /// </summary>
    public static long QueueRevision(JsonElement state) =>
        state.ValueKind == JsonValueKind.Object
        && state.TryGetProperty("queueRevision", out var revision)
        && revision.ValueKind == JsonValueKind.Number
            ? revision.GetInt64()
            : 0;
}
