using System.Text.Json;

namespace Harmony.Cloud.Api.Domain;

public static class PlaybackPayload
{
    private const int MaximumBytes = 1024 * 1024;

    public static bool IsPortable(JsonElement payload)
    {
        // Queue metadata can legitimately contain hundreds of songs. It remains
        // sanitized and bounded, but must not be confused with audio/file data.
        return payload.GetRawText().Length <= MaximumBytes && !ContainsForbiddenKey(payload);
    }

    private static bool ContainsForbiddenKey(JsonElement value) => value.ValueKind switch
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
}
