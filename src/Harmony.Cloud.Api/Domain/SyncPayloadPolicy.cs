using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Harmony.Cloud.Api.Domain;

/// <summary>
/// Keeps transient and device-specific values out of Cloud. Display metadata
/// (title, artists, album, duration, artwork) is deliberately preserved so a
/// device syncing from Cloud gets the same library a local backup would give
/// it; only stream URLs and local file paths are dropped, as defense in depth
/// behind the client's own payload sanitizer.
/// </summary>
public static partial class SyncPayloadPolicy
{
    private static readonly HashSet<string> TransientLocationProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "streamUrl", "audioUrl", "mediaUrl", "streamInfo",
        "filePath", "localPath", "downloadPath"
    };

    /// <summary>Artwork containers whose nested <c>url</c> is a stable remote
    /// reference rather than an expiring stream, and must survive.</summary>
    private static readonly HashSet<string> ArtworkContainerProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbnails", "thumbnail", "artwork", "artUri", "image"
    };

    public static JsonDocument Normalize(string entityType, string entityId, JsonElement payload)
    {
        if (IsSongEntity(entityType) && !IsVideoId(entityId))
            throw new InvalidDataException("invalid_video_id");

        var node = JsonNode.Parse(payload.GetRawText()) ?? throw new InvalidDataException("invalid_sync_payload");
        StripTransientLocations(node, insideArtwork: false);
        return JsonDocument.Parse(node.ToJsonString());
    }

    public static bool IsVideoId(string value) => VideoIdPattern().IsMatch(value);

    private static bool IsSongEntity(string entityType) =>
        string.Equals(entityType, "song", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entityType, "track", StringComparison.OrdinalIgnoreCase);

    private static void StripTransientLocations(JsonNode node, bool insideArtwork)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array.Where(item => item is not null))
                    StripTransientLocations(item!, insideArtwork);
                return;
            case JsonObject objectNode:
                StripObject(objectNode, insideArtwork);
                return;
            default:
                return;
        }
    }

    private static void StripObject(JsonObject objectNode, bool insideArtwork)
    {
        if (objectNode["videoId"] is { } videoId
            && videoId.GetValueKind() == JsonValueKind.String
            && !IsVideoId(videoId.GetValue<string>()))
            throw new InvalidDataException("invalid_video_id");

        foreach (var property in objectNode.ToArray())
        {
            var isArtworkUrl = insideArtwork
                && string.Equals(property.Key, "url", StringComparison.OrdinalIgnoreCase);
            if (!isArtworkUrl && TransientLocationProperties.Contains(property.Key))
            {
                objectNode.Remove(property.Key);
                continue;
            }
            if (property.Value is not null)
                StripTransientLocations(
                    property.Value,
                    insideArtwork || ArtworkContainerProperties.Contains(property.Key));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();
}
